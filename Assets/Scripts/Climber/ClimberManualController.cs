using UnityEngine;

namespace PhysicalAI.Climber
{
    // Owns input and controller arbitration, never a Rigidbody or a joint.
    [DefaultExecutionOrder(-150)]
    public sealed class ClimberManualController : MonoBehaviour
    {
        [SerializeField] ClimberBody[] bodies;
        [SerializeField] ClimberScriptController[] scripts;
        [SerializeField] ClimberGoal goal;
        public float DefaultEffort = .70f, EffortStep = .05f, TargetMoveSpeed = .30f;
        public int Selection {get;private set;} // 0 left, 1 right, 2 both
        public bool ScriptMode {get;private set;}
        public ClimberBody FocusBody => bodies!=null && bodies.Length>0?bodies[0]:null;
        public bool InGoal => FocusBody!=null && goal.Contains(FocusBody.GetBodyState());
        public bool Arrived => goalStable>=.3f;
        readonly Vector3[] targets=new Vector3[2];
        readonly float[] efforts=new float[2];
        readonly bool[] requested=new bool[2], previousGrabbed=new bool[2], needsRearm=new bool[2];
        BodyReset[] initial;
        float goalStable;
        string notice="Manual mode. Click the Game view to focus keyboard input.";

        public void Configure(ClimberBody[] values)
        {
            bodies=values;scripts=new ClimberScriptController[values.Length];
            for(int i=0;i<values.Length;i++)scripts[i]=values[i].GetComponent<ClimberScriptController>();
            goal=values[0].GetComponent<ClimberGoal>();
            if(Application.isPlaying)Initialize();
        }
        void Awake() {Initialize();}
        void Initialize()
        {
            if(FocusBody==null)return;
            initial=new BodyReset[bodies.Length];
            for(int i=0;i<bodies.Length;i++)
            {
                var state=bodies[i].GetBodyState();
                initial[i]=new BodyReset {Torso=state.Position,Rotation=state.Rotation,
                    LeftHand=bodies[i].GetHandState(HandId.Left).Position,RightHand=bodies[i].GetHandState(HandId.Right).Position};
                if(scripts[i]!=null)scripts[i].enabled=false;
            }
            SynchronizeManualTargets();
        }
        void SynchronizeManualTargets()
        {
            var state=FocusBody.GetBodyState();
            for(int i=0;i<2;i++)
            {
                var h=FocusBody.GetHandState((HandId)i);
                targets[i]=Quaternion.Inverse(state.Rotation)*(h.Position-h.ShoulderPosition);
                efforts[i]=DefaultEffort;requested[i]=false;previousGrabbed[i]=h.Grabbed;needsRearm[i]=false;
            }
            goalStable=0;
        }
        void Update()
        {
            if(FocusBody==null)return;
            if(Input.GetKeyDown(KeyCode.F1))SetScriptMode(!ScriptMode);
            if(Input.GetKeyDown(KeyCode.R))ResetToWall();
            if(ScriptMode)return;
            if(Input.GetKeyDown(KeyCode.Alpha1)||Input.GetKeyDown(KeyCode.Keypad1))SelectHands(0);
            if(Input.GetKeyDown(KeyCode.Alpha2)||Input.GetKeyDown(KeyCode.Keypad2))SelectHands(1);
            if(Input.GetKeyDown(KeyCode.Alpha3)||Input.GetKeyDown(KeyCode.Keypad3))SelectHands(2);
            Vector3 delta=new Vector3(
                (Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),
                (Input.GetKey(KeyCode.E)?1:0)-(Input.GetKey(KeyCode.Q)?1:0),
                (Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0));
            MoveTarget(Vector3.ClampMagnitude(delta,1)*TargetMoveSpeed*Time.deltaTime);
            if(Input.mouseScrollDelta.y!=0)AdjustEffort(Input.mouseScrollDelta.y);
            if(Input.GetKeyDown(KeyCode.Space))ToggleGrab();
        }
        bool Selected(int i) => Selection==2 || Selection==i;
        public void SelectHands(int selection) {Selection=Mathf.Clamp(selection,0,2);}
        public void MoveTarget(Vector3 bodyLocalDelta)
        {
            if(ScriptMode)return;
            for(int i=0;i<2;i++)if(Selected(i))
            {
                Vector3 v=targets[i]+bodyLocalDelta;
                targets[i]=v.sqrMagnitude<1e-8f?Vector3.up*.15f:v.normalized*Mathf.Clamp(v.magnitude,.15f,.85f);
            }
        }
        public void AdjustEffort(float wheelNotches)
        {
            if(ScriptMode)return;
            for(int i=0;i<2;i++)if(Selected(i))efforts[i]=Mathf.Clamp01(efforts[i]+wheelNotches*EffortStep);
        }
        public void ToggleGrab()
        {
            if(ScriptMode)return;
            // Both-hand selection uses one common on/off intent, not inversion
            // that would release the supporting hand when only the other is off.
            bool bothValue=!(requested[0]&&requested[1]);
            for(int i=0;i<2;i++)if(Selected(i))
            {
                requested[i]=Selection==2?bothValue:!requested[i];
                if(!requested[i])needsRearm[i]=false;
                FocusBody.SetGrab((HandId)i,requested[i]);
            }
        }
        public Vector3 Target(HandId hand) => targets[(int)hand];
        public float Effort(HandId hand) => efforts[(int)hand];
        public bool GrabRequested(HandId hand) => requested[(int)hand];
        public string GripReason(HandId hand)
        {
            int i=(int)hand;var h=FocusBody.GetHandState(hand);
            if(h.Grabbed)return "GRIPPED";
            if(h.Cooldown>0)return "Cooldown";
            if(needsRearm[i]||h.NeedsRearm)return "Release request, then press Space again (rearm)";
            if(ScriptMode)return FocusBody.GetGripCandidate(hand).Reason;
            if(!requested[i])return "Request OFF";
            var candidate=FocusBody.GetGripCandidate(hand);
            return candidate.Allowed?"Valid contact; awaiting physics step":candidate.Reason;
        }
        void FixedUpdate() {SubmitCommands(Time.fixedDeltaTime);}
        internal void SubmitCommands(float dt)
        {
            if(FocusBody==null)return;
            if(!ScriptMode)
            {
                for(int i=0;i<2;i++)
                {
                    var h=FocusBody.GetHandState((HandId)i);
                    if(previousGrabbed[i]&&!h.Grabbed&&requested[i])needsRearm[i]=true;
                    previousGrabbed[i]=h.Grabbed;
                    FocusBody.SetHandTarget((HandId)i,targets[i],efforts[i]);
                    // Do not resend true every tick: the body's rearm state is authoritative.
                }
            }
            goalStable=InGoal?goalStable+dt:0;
        }
        public void ResetToWall()
        {
            if(initial==null)return;
            for(int i=0;i<bodies.Length;i++)
            {
                bodies[i].ResetBody(initial[i]);
                if(scripts[i]!=null)scripts[i].RestartController();
            }
            SynchronizeManualTargets();
            notice="Reset to wall base; grab requests OFF.";
        }
        public void SetScriptMode(bool useScript)
        {
            // Switching intentionally restarts the comparison from the same pose.
            foreach(var script in scripts)if(script!=null)script.enabled=false;
            ScriptMode=useScript;ResetToWall();
            foreach(var script in scripts)if(script!=null)script.enabled=useScript;
            notice=useScript?"SCRIPT: 16 arenas running. F1 returns to manual and resets.":"MANUAL: arena 0 only. F1 restarts scripted comparison.";
        }
        void OnGUI()
        {
            if(FocusBody==null)return;
            float width=Mathf.Clamp(Screen.width*.30f,270,430);
            GUI.Box(new Rect(8,8,width,Mathf.Min(Screen.height-16,650)),GUIContent.none);
            GUILayout.BeginArea(new Rect(18,16,width-20,Screen.height-28));
            GUILayout.Label("LAYER 1  |  "+(ScriptMode?"SCRIPT":"MANUAL"));
            GUILayout.Label("Selected: "+(Selection==2?"BOTH":Selection==0?"LEFT":"RIGHT"));
            GUILayout.Label("1 Left  2 Right  3 Both\nW/S Forward/Back  A/D Left/Right\nE Up  Q Down  |  Wheel effort +/-0.05\nSpace Grab intent  R Reset  F1 Manual/Script");
            GUILayout.Space(8);
            for(int i=0;i<2;i++)
            {
                var id=(HandId)i;var h=FocusBody.GetHandState(id);
                GUILayout.Label($"{id}: {(h.Grabbed?"GRIPPED":"FREE")}  cooldown {h.Cooldown:F2}s");
                GUILayout.Label($"Request {(h.GrabRequested?"ON":"OFF")} | effort {(ScriptMode?h.Effort:efforts[i]):F2} (applied {h.Effort:F2})\nActive force {h.ForceFraction:P0} | arm {h.ArmLength:F2}m");
                GUILayout.Label($"Target shoulder-local: {(ScriptMode?h.TargetOffset:targets[i]):F2}\nHand arena-local: {h.Position:F2}");
                GUILayout.Label("Grab: "+GripReason(id));
                GUILayout.Space(6);
            }
            var b=FocusBody.GetBodyState();
            GUILayout.Label($"Torso height {b.Position.y:F2}m | arena {b.Position:F2}\nGoal center inside + speed valid: {InGoal}\nArrived (0.30s stable): {Arrived}");
            GUILayout.Label(notice);
            GUILayout.Label("Held hand: Q lifts torso, E lowers it.\nAfter clearing the edge, S moves torso inward.\nNever release the only supporting hand.");
            GUILayout.EndArea();
        }
    }
}
