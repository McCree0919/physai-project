using System;
using System.Collections.Generic;
using PhysicalAI.Climber;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace PhysicalAI.Learning
{
    public enum ClimbTargetMode {Incremental=0,Absolute=1}
    public sealed partial class ClimberAgent : Agent
    {
        public const int ObservationCount=327;
        public const float FreeEffort=1f,GripEffort=.7f,AbsoluteReach=.85f;
        // Default stays Incremental so pilot results reproduce; trainer YAML overrides via environment parameter "target_mode".
        public ClimbTargetMode TargetMode=ClimbTargetMode.Incremental;
        public ClimbTargetMode ActiveTargetMode {get;private set;}
        // Height shaping (LAYER3_HEIGHT_SHAPING): scale per metre of new episode-best torso height, total capped below the progress cap.
        // Default off so earlier results reproduce; trainer YAML overrides via "height_reward_scale" / "height_reward_cap".
        public const float ProgressRewardCap=.3f;
        public float HeightRewardScale,HeightRewardCap;
        // Snapshot recording only: an external expert drives the body; the Agent tracks task state but never writes commands.
        public bool ExternalCommand;
        ClimberBody body;ClimberGoal goal;BodyReset spawn;
        readonly Vector3[] targets=new Vector3[2];
        readonly RaycastHit[] hits=new RaycastHit[64];
        readonly List<float> observations=new List<float>(ObservationCount);
        readonly HashSet<int> paidSupports=new HashSet<int>();
        readonly bool[] previousGrip=new bool[2];
        float elapsed,goalStable,supportStable,groundStable,brokenTime=-1,highestSupport,progressReward;
        float rewardTime,rewardTerminal,maximumHeight,heightScale,heightCap,heightBest,heightReward;
        int supportId,decisions,progressEvents;
        bool active,level1,level2,subscribed;
        public int Decisions => decisions;
        public float Elapsed => elapsed;
        public int ProgressEvents => progressEvents;
        public float HeightReward => heightReward;
        public override void Initialize()
        {
            body=GetComponent<ClimberBody>();goal=GetComponent<ClimberGoal>();
            if(body==null||goal==null)throw new InvalidOperationException("ClimberAgent requires the public body and goal components");
            var b=body.GetBodyState();spawn=new BodyReset {Torso=b.Position,LeftHand=body.GetHandState(HandId.Left).Position,RightHand=body.GetHandState(HandId.Right).Position,Rotation=b.Rotation};
            MaxStep=0; // Explicit 60s/600-decision task timeout, never SDK truncation.
            Academy.Instance.AgentPreStep+=BeforePhysics;subscribed=true;
        }
        protected override void OnDisable()
        {
            if(subscribed&&Academy.IsInitialized)Academy.Instance.AgentPreStep-=BeforePhysics;
            subscribed=false;base.OnDisable();
        }
        public override void OnEpisodeBegin()
        {
            float mode=Academy.Instance.EnvironmentParameters.GetWithDefault("target_mode",(float)TargetMode);
            if(mode!=0&&mode!=1)throw new InvalidOperationException("target_mode must be 0 (incremental) or 1 (absolute): "+mode);
            ActiveTargetMode=(ClimbTargetMode)(int)mode;
            heightScale=Academy.Instance.EnvironmentParameters.GetWithDefault("height_reward_scale",HeightRewardScale);
            heightCap=Academy.Instance.EnvironmentParameters.GetWithDefault("height_reward_cap",HeightRewardCap);
            if(heightScale<0||heightCap<0||heightCap>=ProgressRewardCap)throw new InvalidOperationException($"height_reward_scale must be >=0 and height_reward_cap in [0,{ProgressRewardCap}): {heightScale} {heightCap}");
            body.ResetBody(spawn);Physics.SyncTransforms();
            elapsed=goalStable=supportStable=groundStable=highestSupport=progressReward=rewardTime=rewardTerminal=heightReward=0;
            maximumHeight=spawn.Torso.y;brokenTime=-1;supportId=0;decisions=progressEvents=0;active=true;level1=level2=false;paidSupports.Clear();
            var b=body.GetBodyState();
            for(int i=0;i<2;i++)
            {
                var h=body.GetHandState((HandId)i);targets[i]=Project(Quaternion.Inverse(b.Rotation)*(h.Position-h.ShoulderPosition));previousGrip[i]=false;
                body.SetHandTarget((HandId)i,targets[i],FreeEffort);body.SetGrab((HandId)i,false);
            }
            SnapshotSpawn();
            heightBest=body.GetBodyState().Position.y; // Ground spawn or restored snapshot: only height gained in this episode pays.
        }
        public static Vector3 Project(Vector3 q)
        {float length=q.magnitude;return length<.0001f?Vector3.up*.15f:q*(Mathf.Clamp(length,.15f,.85f)/length);}
        // Absolute: the unit action ball maps linearly onto the 0.85m reach ball, no accumulated state.
        public static Vector3 AbsoluteTarget(Vector3 action) => Project(Vector3.ClampMagnitude(action,1)*AbsoluteReach);
        public void ApplyCommand(Vector3 left,Vector3 right,int grabLeft,int grabRight)
        {
            if(!active)return;
            var commands=new[] {left,right};var grabs=new[] {grabLeft,grabRight};decisions++;
            for(int i=0;i<2;i++)
            {
                if(ExternalCommand)break;
                targets[i]=ActiveTargetMode==ClimbTargetMode.Absolute?AbsoluteTarget(commands[i]):Project(targets[i]+Vector3.ClampMagnitude(commands[i],1)*.08f);
                var h=body.GetHandState((HandId)i);
                body.SetHandTarget((HandId)i,targets[i],h.Grabbed?GripEffort:FreeEffort);
                body.SetGrab((HandId)i,grabs[i]==1); // State intent, no toggle or automatic rearming.
            }
            AddReward(-.0002f);rewardTime-=.0002f;
            if(decisions>=600)Finish(false,"Timeout");
        }
        public override void OnActionReceived(ActionBuffers a)
        {
            var c=a.ContinuousActions;var d=a.DiscreteActions;
            ApplyCommand(new Vector3(Mathf.Clamp(c[0],-1,1),Mathf.Clamp(c[1],-1,1),Mathf.Clamp(c[2],-1,1)),new Vector3(Mathf.Clamp(c[3],-1,1),Mathf.Clamp(c[4],-1,1),Mathf.Clamp(c[5],-1,1)),d[0],d[1]);
        }
        public override void Heuristic(in ActionBuffers a)
        {a.ContinuousActions.Clear();a.DiscreteActions.Clear();}
        internal void BeforePhysics(int step)
        {
            if(!active)return;
            float dt=Time.fixedDeltaTime;elapsed+=dt;var b=body.GetBodyState();maximumHeight=Mathf.Max(maximumHeight,b.Position.y);
            if(heightScale>0&&b.Position.y>heightBest)
            {
                // Running maximum: each metre is paid once per episode, so oscillating below the best height earns nothing.
                float r=Mathf.Min(heightScale*(b.Position.y-heightBest),heightCap-heightReward);heightBest=b.Position.y;
                if(r>0){heightReward+=r;AddReward(r);}
            }
            for(int i=0;i<2;i++)
            {
                var h=body.GetHandState((HandId)i);
                if(previousGrip[i]&&!h.Grabbed&&h.GrabRequested&&h.NeedsRearm)brokenTime=0;
                previousGrip[i]=h.Grabbed;
                if(!ExternalCommand)body.SetHandTarget((HandId)i,targets[i],h.Grabbed?GripEffort:FreeEffort);
            }
            bool supported=TrySupport(out int id,out float height)&&b.Velocity.magnitude<.25f;
            if(supported)
            {
                if(id!=supportId){supportId=id;supportStable=0;}
                supportStable+=dt;
                if(supportStable>=.3f)
                {
                    if(height>=.45f)level1=true;if(height>=.95f)level2=true;
                    if(height>=highestSupport+.2f&&!paidSupports.Contains(id))
                    {
                        highestSupport=height;paidSupports.Add(id);
                        if(progressEvents<3){progressEvents++;progressReward+=.1f;AddReward(.1f);}
                    }
                }
            }
            else {supportStable=0;supportId=0;}
            bool onGround=supported&&height<.1f&&b.GrabbedHands==0;
            groundStable=level1&&onGround?groundStable+dt:0;
            if(b.GrabbedHands>0||supported)brokenTime=-1;else if(brokenTime>=0)brokenTime+=dt;
            goalStable=goal.Contains(b)?goalStable+dt:0;
            // Metric only: reaching the goal counts as level 2 even when the episode ends before the 0.3s stable-support check; rewards unchanged.
            if(goalStable>=.3f){level2=true;Finish(true,"Success");return;}
            if(b.Position.y<-.3f||groundStable>=.3f){Finish(false,"Fall");return;}
            if(Mathf.Abs(b.Position.x)>6||b.Position.z < -4||b.Position.z>8){Finish(false,"OutOfBounds");return;}
            if(brokenTime>=2){Finish(false,"UnrecoverableGrip");return;}
            if(elapsed>=60)Finish(false,"Timeout");
        }
        public bool TrySupport(out int id,out float height)
        {
            var b=body.GetBodyState();id=0;height=0;var inv=Quaternion.Inverse(b.Rotation);
            var samples=new[] {Vector3.zero,new Vector3(-.3f,0,-.175f),new Vector3(.3f,0,-.175f),new Vector3(-.3f,0,.175f),new Vector3(.3f,0,.175f)};
            for(int i=0;i<samples.Length;i++)
            {
                var origin=transform.TransformPoint(b.Position+b.Rotation*samples[i]);
                if(!ClimbLocalGeometry.Cast(transform,origin,-transform.up,.485f,hits,out var hit)||hit.distance<.395f||Vector3.Dot(hit.normal,transform.up)<.95f)return false;
                float y=transform.InverseTransformPoint(hit.point).y;
                if(i==0){id=hit.collider.GetInstanceID();height=y;}else if(Mathf.Abs(y-height)>.02f)return false;
            }
            return true;
        }
        void Finish(bool success,string reason)
        {
            if(!active)return;active=false;rewardTerminal=success?1:-1;AddReward(rewardTerminal);
            var recorder=Academy.Instance.StatsRecorder;
            recorder.Add("Climb/Success",success?1:0);recorder.Add("Climb/Level1Rate",level1?1:0);recorder.Add("Climb/Level2Rate",level2?1:0);
            recorder.Add("Climb/Decisions",decisions);recorder.Add("Climb/SimSeconds",elapsed);recorder.Add("Climb/MaximumTorsoHeight",maximumHeight);
            recorder.Add("Climb/RewardProgress",progressReward);recorder.Add("Climb/RewardTime",rewardTime);recorder.Add("Climb/RewardTerminal",rewardTerminal);
            if(heightScale>0)recorder.Add("Climb/RewardHeight",heightReward);
            foreach(string label in new[] {"Success","Fall","OutOfBounds","UnrecoverableGrip","Timeout"})recorder.Add("Climb/End/"+label,reason==label?1:0);
            if(spawnStage!=null)recorder.Add("Climb/Spawn/"+spawnStage+"/Success",success?1:0);
            SpawnSchedule?.Report(spawnStage??"ground",success,level1,level2);
            ClimbEpisodeLog.Write(name,success,level1,level2,decisions,elapsed,progressReward+rewardTime+rewardTerminal+heightReward,reason,SpawnSource);
            EndEpisode();
        }
        void Add(Vector3 value,float scale=1)
        {observations.Add(Mathf.Clamp(value.x/scale,-1,1));observations.Add(Mathf.Clamp(value.y/scale,-1,1));observations.Add(Mathf.Clamp(value.z/scale,-1,1));}
        public float[] BuildObservations()
        {
            observations.Clear();var b=body.GetBodyState();var inv=Quaternion.Inverse(b.Rotation);
            Add(inv*b.Velocity,5);Add(inv*b.AngularVelocity,10);Add(inv*transform.InverseTransformDirection(Physics.gravity.normalized));Add(inv*(goal.Center-b.Position),6);observations.Add(Mathf.Clamp01(1-elapsed/60));
            for(int i=0;i<2;i++)
            {
                var h=body.GetHandState((HandId)i);Add(inv*(h.Position-h.ShoulderPosition),.9f);
                Add(inv*(h.Velocity-b.Velocity-Vector3.Cross(b.AngularVelocity,h.ShoulderPosition-b.Position)),5);Add(targets[i],.9f);
                observations.Add(Mathf.Clamp01(h.ArmLength/.9f));observations.Add(Mathf.Clamp01(h.Effort));Add(inv*h.CommandForce,450);
                observations.Add(h.Grabbed?1:0);observations.Add(h.GrabRequested?1:0);observations.Add(h.NeedsRearm?1:0);observations.Add(Mathf.Clamp01(h.Cooldown/.3f));
                var candidate=body.GetGripCandidate((HandId)i);bool present=candidate.Normal.sqrMagnitude>.5f;
                observations.Add(present?1:0);observations.Add(candidate.Allowed?1:0);Add(present?inv*(candidate.Point-h.Position):Vector3.zero,.9f);Add(present?inv*candidate.Normal:Vector3.zero);
            }
            Vector3 rayOrigin=transform.TransformPoint(b.Position+b.Rotation*Vector3.up*.28f);
            for(int pitch=-45;pitch<=45;pitch+=45)for(int yaw=-120;yaw<=120;yaw+=30)
            {
                Vector3 direction=transform.TransformDirection(b.Rotation*Quaternion.Euler(pitch,yaw,0)*Vector3.forward);
                bool hit=ClimbLocalGeometry.Cast(transform,rayOrigin,direction,2,hits,out var result);
                observations.Add(hit?1:0);observations.Add(hit?Mathf.Clamp01(result.distance/2):0);
                Add(hit?inv*transform.InverseTransformDirection(result.normal):Vector3.zero);
                observations.Add(hit&&result.collider.GetComponent<ClimberSurface>()!=null?1:0);
            }
            for(int z=-2;z<=2;z++)for(int x=-2;x<=2;x++)
            {
                var origin=transform.TransformPoint(b.Position+b.Rotation*new Vector3(x*.4f,1.5f,z*.4f));
                bool hit=ClimbLocalGeometry.Cast(transform,origin,-transform.up,3,hits,out var result);
                observations.Add(hit?1:0);observations.Add(hit?Mathf.Clamp((transform.InverseTransformPoint(result.point).y-b.Position.y)/1.5f,-1,1):0);
                float normal=hit?Vector3.Dot(result.normal,transform.up):0;observations.Add(normal);
                observations.Add(hit&&normal>=.7071f&&result.collider.GetComponent<ClimberSurface>()!=null&&ClimbLocalGeometry.Clear(transform,result.collider,result.point)?1:0);
            }
            if(observations.Count!=ObservationCount)throw new InvalidOperationException("Observation dimension "+observations.Count);
            return observations.ToArray();
        }
        public override void CollectObservations(VectorSensor sensor){foreach(float value in BuildObservations())sensor.AddObservation(value);}
    }
}
