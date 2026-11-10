using PhysicalAI.Climber;
using PhysicalAI.Climber.Layer2Prep;
using PhysicalAI.Structures;
using UnityEngine;

namespace PhysicalAI.Demo
{
    public enum DemoStructure {RungWall,SingleBox,TwoStepBoxes}

    // Scheduling adapter for the unchanged scripted controllers (as StructureScriptDriver / PrepExperimentRunner do):
    // rung walls step PrepClimbController every 5 physics steps; boxes run BoxClimbSmokeController.
    // After arrival (or failure) it holds 2 s, releases both grips, then resets the body and starts a fresh controller.
    [DefaultExecutionOrder(-100)]
    public sealed class DemoArena : MonoBehaviour,IDemoArenaStatus
    {
        public const float HoldSeconds=2,ReleaseSeconds=.3f,RungSpawnX=-.01f; // -0.01: the scanned yaw-0 replica
        [SerializeField] DemoStructure structure;
        [SerializeField] float heightGap,lateralGap;
        [SerializeField] string title,parameters;
        [SerializeField] ClimberBody body;
        [SerializeField] TextMesh label;
        [SerializeField] Vector3 startTorso,startLeft,startRight;
        PrepWall wall;PrepClimbController rung;BoxClimbSmokeController box;
        int ticks;float doneTime=-1;bool released,lastSuccess;
        public string Title => title;
        public string Parameters => parameters;
        public int Completions {get;private set;}
        public int Failures {get;private set;}
        public float Elapsed => structure==DemoStructure.RungWall?rung?.Elapsed??0:box!=null?box.Elapsed:0;
        public string Status => doneTime>=0?(lastSuccess?"ARRIVED - resetting":"failed - resetting"):$"climbing {Elapsed:F1} s";
        public void Configure(DemoStructure kind,float height,float spacing,string heading,string details,ClimberBody value,TextMesh text,BodyReset start)
        {
            structure=kind;heightGap=height;lateralGap=spacing;title=heading;parameters=details;body=value;label=text;
            startTorso=start.Torso;startLeft=start.LeftHand;startRight=start.RightHand;
        }
        void Start(){Begin();}
        void Begin()
        {
            doneTime=-1;released=false;ticks=0;
            if(structure==DemoStructure.RungWall)
            {
                if(wall==null)wall=new PrepWall(body,heightGap,lateralGap);
                wall.Reset(RungSpawnX);rung=new PrepClimbController(wall);
            }
            else
            {
                body.ResetBody(new BodyReset {Torso=startTorso,LeftHand=startLeft,RightHand=startRight,Rotation=Quaternion.identity});
                if(box!=null){box.enabled=false;Destroy(box);}
                box=gameObject.AddComponent<BoxClimbSmokeController>();box.Configure(body,structure==DemoStructure.TwoStepBoxes);
            }
            Physics.SyncTransforms();Refresh();
        }
        void FixedUpdate()
        {
            float dt=Time.fixedDeltaTime;
            if(doneTime<0)
            {
                if(structure==DemoStructure.RungWall)
                {
                    if(++ticks%5==0)rung.Step(dt*5);
                    if(rung.Done)Finish(rung.Success);
                }
                else if(box.Completed||box.Failed)Finish(box.Completed);
                return;
            }
            doneTime+=dt;
            if(doneTime>=HoldSeconds&&!released){body.SetGrab(HandId.Left,false);body.SetGrab(HandId.Right,false);released=true;}
            if(doneTime>=HoldSeconds+ReleaseSeconds)Begin();
        }
        void Finish(bool success)
        {
            lastSuccess=success;doneTime=0;
            if(success)Completions++;else Failures++;
            Debug.Log($"DEMO_ARENA_DONE {name} success={success} seconds={Elapsed:F2} completed={Completions} failed={Failures}");
            Refresh();
        }
        void Refresh(){if(label!=null)label.text=$"{title}\n{parameters}\ncompleted {Completions}"+(Failures>0?$"  failed {Failures}":"");}
    }
}
