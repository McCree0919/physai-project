using System;
using UnityEngine;

namespace PhysicalAI.Climber
{
    public enum HandId { Left, Right }
    public struct BodyState
    {
        public Vector3 Position, Velocity, AngularVelocity;
        public Quaternion Rotation;
        public int GrabbedHands;
    }
    public struct HandState
    {
        public Vector3 Position, Velocity, ShoulderPosition, TargetOffset, CommandForce;
        public Quaternion Rotation;
        public bool Grabbed, GrabRequested, NeedsRearm;
        public float ArmLength, Effort, ForceFraction, PeakConstraintLoad, Cooldown;
    }
    public struct GripCandidate
    {
        public bool Allowed;
        public Vector3 Point, Normal;
        public string Reason;
        internal Collider Collider;
    }
    public struct BodyReset
    {
        public Vector3 Torso, LeftHand, RightHand;
        public Quaternion Rotation;
    }

    [DefaultExecutionOrder(0)]
    public sealed partial class ClimberBody : MonoBehaviour
    {
        [SerializeField] Rigidbody torso;
        [SerializeField] Rigidbody[] hands = new Rigidbody[2];
        [SerializeField] Vector3[] shoulders = { new Vector3(-.3f,.28f,0), new Vector3(.3f,.28f,0) };
        public float ArmLength = .9f;
        public float PullForce = 450, FreeForce = 40, Spring = 900, Damper = 70;
        public float TargetSpeed = 1.5f;
        public bool LockRotationX = true, LockRotationY, LockRotationZ = true;
        public LayerMask GrabbableMask = 1 << 8;
        public bool LogDiagnostics;
        readonly ConfigurableJoint[] arms = new ConfigurableJoint[2];
        readonly FixedJoint[] grips = new FixedJoint[2];
        readonly Vector3[] requested = new Vector3[2], targets = new Vector3[2], forces = new Vector3[2];
        readonly float[] effort = new float[2], appliedEffort = new float[2], cooldown = new float[2], peak = new float[2];
        readonly bool[] wantsGrab = new bool[2], armed = {true,true}, held = new bool[2];
        readonly Collider[] captured = new Collider[2];
        float diagnosticTime;
        internal Rigidbody Torso => torso;
        internal Rigidbody Hand(int id) => hands[id];
        internal ConfigurableJoint Arm(int id) => arms[id];
        internal FixedJoint Grip(int id) => grips[id];

        internal void Configure(Rigidbody trunk, Rigidbody left, Rigidbody right)
        {
            torso = trunk; hands = new[] {left,right};
            ApplyRotationLocks();
            for (int i=0;i<2;i++)
            {
                var joint = hands[i].gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = torso;
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = Vector3.zero;
                joint.connectedAnchor = torso.transform.InverseTransformVector(torso.rotation * shoulders[i]);
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Limited;
                joint.linearLimit = new SoftJointLimit { limit = ArmLength, contactDistance = .01f };
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
                joint.breakForce = joint.breakTorque = Mathf.Infinity;
                joint.enableCollision = false;
                joint.projectionMode = JointProjectionMode.None;
                arms[i] = joint;
                requested[i] = targets[i] = Quaternion.Inverse(torso.rotation) * (hands[i].position-Shoulder(i));
            }
            Physics.IgnoreCollision(hands[0].GetComponent<Collider>(),hands[1].GetComponent<Collider>());
        }
        void Awake()
        {
            // Scene serialization restores physical joints; collect references after load.
            if (torso != null && hands[0] != null)
            {
                for (int i=0;i<2;i++)
                {
                    arms[i]=hands[i].GetComponent<ConfigurableJoint>();
                    requested[i]=targets[i]=Quaternion.Inverse(torso.rotation)*(hands[i].position-Shoulder(i));
                }
                Physics.IgnoreCollision(hands[0].GetComponent<Collider>(),hands[1].GetComponent<Collider>());
            }
        }
        void ApplyRotationLocks()
        {
            torso.constraints = (LockRotationX?RigidbodyConstraints.FreezeRotationX:0)
                | (LockRotationY?RigidbodyConstraints.FreezeRotationY:0)
                | (LockRotationZ?RigidbodyConstraints.FreezeRotationZ:0);
        }
        Vector3 Shoulder(int i) => torso.position + torso.rotation * shoulders[i];
        public void SetHandTarget(HandId hand, Vector3 offsetFromShoulderBodyLocal, float effort01)
        {
            if (!IsFinite(offsetFromShoulderBodyLocal) || float.IsNaN(effort01) || float.IsInfinity(effort01))
                throw new ArgumentException("Nonfinite body command");
            int i=(int)hand;
            float length=offsetFromShoulderBodyLocal.magnitude;
            requested[i] = length < .001f ? Vector3.up*.15f : offsetFromShoulderBodyLocal * (Mathf.Clamp(length,.15f,.85f)/length);
            effort[i]=Mathf.Clamp01(effort01);
        }
        static bool IsFinite(Vector3 p) => !float.IsNaN(p.x+p.y+p.z) && !float.IsInfinity(p.x+p.y+p.z);
        public void SetGrab(HandId hand, bool requestedGrab)
        {
            int i=(int)hand;
            wantsGrab[i]=requestedGrab;
            if (!requestedGrab) { Release(i,.2f); armed[i]=true; }
        }
        void Release(int i,float delay)
        {
            if (!held[i] && grips[i]==null) return;
            held[i]=false; captured[i]=null; cooldown[i]=delay;
            if (grips[i]!=null)
            {
                // Break at the next solve, even before deferred component destruction.
                grips[i].breakForce=0; grips[i].breakTorque=0;
                Destroy(grips[i]); grips[i]=null;
            }
        }
        public GripCandidate GetGripCandidate(HandId hand)
        {
            int i=(int)hand;
            var result=new GripCandidate { Reason="NoContact" };
            foreach(var c in Physics.OverlapSphere(hands[i].position,.111f,~0,QueryTriggerInteraction.Ignore))
            {
                if(c.transform.IsChildOf(transform) && c.attachedRigidbody!=null) continue;
                var box=c as BoxCollider;
                var surface=c.GetComponent<ClimberSurface>();
                if(surface==null) continue;
                result=CheckCandidate(i,box,surface);
                if(result.Allowed) return result;
            }
            return result;
        }
        internal GripCandidate CheckCandidate(int i,BoxCollider box,ClimberSurface surface)
        {
            var result=new GripCandidate { Reason="NotBox", Collider=box };
            if(box==null) return result;
            if((GrabbableMask.value & (1<<box.gameObject.layer))==0) {result.Reason="Layer";return result;}
            if(surface.Arena!=transform) {result.Reason="OtherArena";return result;}
            if(box.attachedRigidbody!=null) {result.Reason="DynamicDeferred";return result;}
            Vector3 point,normal;
            ClimberSurface.TopPoint(box,hands[i].position,transform.up,out point,out normal);
            result.Point=transform.InverseTransformPoint(point);
            result.Normal=transform.InverseTransformDirection(normal);
            float above=Vector3.Dot(hands[i].position-point,normal);
            if(Vector3.Dot(normal,transform.up)<.7071067f || above<-.005f) {result.Reason="NotTop";return result;}
            if(Vector3.Distance(hands[i].position,point)>.111f) {result.Reason="NoContact";return result;}
            if(!GripClearance.IsClear(box,point,transform,torso,hands[0],hands[1])) {result.Reason="TopOccluded";return result;}
            if(hands[i].linearVelocity.magnitude>1f) {result.Reason="TooFast";return result;}
            int other=1-i;
            if(captured[other]==box && Vector3.Distance(hands[i].position,hands[other].position)<.22f)
                {result.Reason="Occupied";return result;}
            result.Allowed=true; result.Reason="TopOrEdge";return result;
        }
        void FixedUpdate() { if(torso!=null) PhysicsStep(Time.fixedDeltaTime); }
        internal void PhysicsStep(float dt)
        {
            for(int i=0;i<2;i++)
            {
                if(arms[i]!=null) peak[i]=Mathf.Max(peak[i],arms[i].currentForce.magnitude);
                cooldown[i]=Mathf.Max(0,cooldown[i]-dt);
                if(held[i] && grips[i]==null) {held[i]=false;captured[i]=null;cooldown[i]=.3f;armed[i]=false;}
                if(!held[i] && wantsGrab[i] && armed[i] && cooldown[i]<=0)
                {
                    var candidate=GetGripCandidate((HandId)i);
                    if(candidate.Allowed)
                    {
                        var grip=hands[i].gameObject.AddComponent<FixedJoint>();
                        grip.connectedBody=null;
                        grip.autoConfigureConnectedAnchor=false;
                        Vector3 point=transform.TransformPoint(candidate.Point);
                        Vector3 anchor=Quaternion.Inverse(hands[i].rotation)*(point-hands[i].position);
                        Vector3 scale=hands[i].transform.lossyScale;
                        grip.anchor=new Vector3(anchor.x/scale.x,anchor.y/scale.y,anchor.z/scale.z);
                        grip.connectedAnchor=point;
                        grip.breakForce=900;grip.breakTorque=90;grip.enableCollision=false;
                        grips[i]=grip;held[i]=true;captured[i]=candidate.Collider;armed[i]=false;
                        gripRotations[i]=hands[i].rotation; // Snapshot bookkeeping only; read by CaptureSnapshot.
                        requested[i]=targets[i]=Quaternion.Inverse(torso.rotation)*(hands[i].position-Shoulder(i));
                    }
                }
                targets[i]=Vector3.MoveTowards(targets[i],requested[i],TargetSpeed*dt);
                appliedEffort[i]=Mathf.MoveTowards(appliedEffort[i],effort[i],dt/.15f);
                Vector3 shoulder=Shoulder(i), r=hands[i].position-shoulder;
                Vector3 desired=shoulder+torso.rotation*targets[i];
                Vector3 relativeVelocity=hands[i].linearVelocity-torso.GetPointVelocity(shoulder);
                // Fixed-step explicit PD, with one shared vector budget.
                Vector3 force=Vector3.ClampMagnitude(Spring*(desired-hands[i].position)-Damper*relativeVelocity,
                    appliedEffort[i]*(held[i]?PullForce:FreeForce));
                forces[i]=force;
                hands[i].AddForce(force,ForceMode.Force);
                torso.AddForceAtPosition(-force,shoulder,ForceMode.Force);
                torso.AddTorque(-Vector3.Cross(r,force),ForceMode.Force);
            }
            diagnosticTime+=dt;
            if(LogDiagnostics && diagnosticTime>=1)
            {
                diagnosticTime=0;
                Debug.Log($"CLIMBER {name} L={GetHandState(HandId.Left).Grabbed} R={GetHandState(HandId.Right).Grabbed} force={forces[0].magnitude/PullForce:F3},{forces[1].magnitude/PullForce:F3} peakConstraint={peak[0]:F2},{peak[1]:F2} N");
            }
        }
        public BodyState GetBodyState() => new BodyState {Position=transform.InverseTransformPoint(torso.position),
            Rotation=Quaternion.Inverse(transform.rotation)*torso.rotation,Velocity=transform.InverseTransformVector(torso.linearVelocity),
            AngularVelocity=transform.InverseTransformVector(torso.angularVelocity),
            GrabbedHands=(held[0] && grips[0]!=null?1:0)+(held[1] && grips[1]!=null?1:0)};
        public HandState GetHandState(HandId hand)
        {
            int i=(int)hand;
            return new HandState {Position=transform.InverseTransformPoint(hands[i].position),
                Velocity=transform.InverseTransformVector(hands[i].linearVelocity),ShoulderPosition=transform.InverseTransformPoint(Shoulder(i)),
                Rotation=Quaternion.Inverse(transform.rotation)*hands[i].rotation,
                TargetOffset=targets[i],CommandForce=transform.InverseTransformVector(forces[i]),Grabbed=held[i] && grips[i]!=null,
                GrabRequested=wantsGrab[i],NeedsRearm=!(held[i] && grips[i]!=null) && !armed[i],
                ArmLength=Vector3.Distance(hands[i].position,Shoulder(i)),Effort=appliedEffort[i],
                ForceFraction=forces[i].magnitude/(held[i]?PullForce:FreeForce),PeakConstraintLoad=peak[i],Cooldown=cooldown[i]};
        }
        public void ResetBody(BodyReset reset)
        {
            for(int i=0;i<2;i++) { Release(i,0);cooldown[i]=0;armed[i]=true;wantsGrab[i]=false;peak[i]=0;effort[i]=appliedEffort[i]=0;forces[i]=Vector3.zero; }
            torso.position=transform.TransformPoint(reset.Torso);torso.rotation=transform.rotation*reset.Rotation;
            for(int i=0;i<2;i++) { hands[i].position=transform.TransformPoint(i==0?reset.LeftHand:reset.RightHand);hands[i].rotation=torso.rotation; }
            torso.linearVelocity=torso.angularVelocity=Vector3.zero;
            ApplyRotationLocks();torso.WakeUp();
            for(int i=0;i<2;i++) { hands[i].linearVelocity=hands[i].angularVelocity=Vector3.zero;requested[i]=targets[i]=Quaternion.Inverse(torso.rotation)*(hands[i].position-Shoulder(i));hands[i].WakeUp(); }
        }
        void OnDrawGizmosSelected()
        {
            if(torso==null) return;
            for(int i=0;i<2;i++) if(hands[i]!=null)
            {
                Gizmos.color=held[i]?Color.green:Color.cyan;Gizmos.DrawWireSphere(Shoulder(i),ArmLength);
                Gizmos.DrawLine(Shoulder(i),hands[i].position);
                Gizmos.color=Color.yellow;Gizmos.DrawWireSphere(Shoulder(i)+torso.rotation*targets[i],.05f);
#if UNITY_EDITOR
                UnityEditor.Handles.Label(hands[i].position+Vector3.up*.2f,$"{(HandId)i} {(held[i]?"GRIP":"FREE")}  active={GetHandState((HandId)i).ForceFraction:P0}  peakConstraint={peak[i]:F1} N");
#endif
            }
        }
    }
}
