using System.Linq;
using PhysicalAI.Climber;
using UnityEngine;

namespace PhysicalAI.Demo
{
    public enum DemoCameraMode {Follow,Overview,AutoCycle}

    // Camera only. Follow: side view across the climbing direction (arena +z), tracking torso height.
    // C cycles Follow / Overview / AutoCycle (next arena every 8 s); arrow keys change the followed arena.
    [RequireComponent(typeof(Camera))]
    public sealed class DemoCamera : MonoBehaviour,IDemoFocus
    {
        public DemoCameraMode Mode=DemoCameraMode.Follow;
        public float CycleSeconds=8,FollowDistance=5.2f,Smoothing=4;
        // Arena-local view direction: mostly from the +x side, slightly in front and above.
        public Vector3 SideDirection=new Vector3(1,.28f,-.42f);
        public int RowLength=4;
        ClimberBody[] bodies;Camera view;int focus;float cycle;bool placed;
        Vector3 smoothTorso;
        public ClimberBody Focus => bodies!=null&&bodies.Length>0?bodies[focus]:null;
        public int FocusIndex => focus;
        public int Count => bodies?.Length??0;
        public string ModeName => Mode==DemoCameraMode.AutoCycle?$"Auto (every {CycleSeconds:F0} s)":Mode.ToString();
        void Start()
        {
            view=GetComponent<Camera>();view.fieldOfView=45;view.nearClipPlane=.05f;view.farClipPlane=300;
            bodies=FindObjectsByType<ClimberBody>(FindObjectsSortMode.None).OrderBy(b=>b.name,System.StringComparer.Ordinal).ToArray();
            if(Focus!=null)smoothTorso=Focus.GetBodyState().Position;
        }
        void Update()
        {
            if(Count==0)return;
            if(Input.GetKeyDown(KeyCode.C)){Mode=(DemoCameraMode)(((int)Mode+1)%3);cycle=0;placed=false;}
            int step=0;
            if(Input.GetKeyDown(KeyCode.RightArrow))step=1;
            if(Input.GetKeyDown(KeyCode.LeftArrow))step=-1;
            if(Input.GetKeyDown(KeyCode.UpArrow))step=RowLength;
            if(Input.GetKeyDown(KeyCode.DownArrow))step=-RowLength;
            if(Mode==DemoCameraMode.AutoCycle&&(cycle+=Time.unscaledDeltaTime)>=CycleSeconds){step=1;cycle=0;}
            if(step!=0)Select(focus+step);
        }
        void Select(int index)
        {
            focus=((index%Count)+Count)%Count;cycle=0;placed=false;
            if(Mode==DemoCameraMode.Overview)Mode=DemoCameraMode.Follow;
            smoothTorso=Focus.GetBodyState().Position;
        }
        void LateUpdate()
        {
            if(Count==0)return;
            Vector3 position;Quaternion rotation;
            if(Mode==DemoCameraMode.Overview)Overview(out position,out rotation);else Follow(out position,out rotation);
            float k=placed?1-Mathf.Exp(-Smoothing*Time.unscaledDeltaTime):1;
            transform.position=Vector3.Lerp(transform.position,position,k);transform.rotation=Quaternion.Slerp(transform.rotation,rotation,k);
            placed=true;
        }
        void Follow(out Vector3 position,out Quaternion rotation)
        {
            var body=Focus;var arena=body.transform;var torso=body.GetBodyState().Position;
            smoothTorso=Vector3.Lerp(smoothTorso,torso,1-Mathf.Exp(-3*Time.unscaledDeltaTime));
            // Keep the arena centre line in frame; follow height and climbing progress (z) of the torso.
            Vector3 center=arena.TransformPoint(new Vector3(0,smoothTorso.y+.25f,Mathf.Clamp(smoothTorso.z,-.4f,1.4f)*.6f+.2f));
            position=center+arena.TransformDirection(SideDirection.normalized)*FollowDistance;
            rotation=Quaternion.LookRotation(center-position,arena.up);
        }
        void Overview(out Vector3 position,out Quaternion rotation)
        {
            var bounds=new Bounds(bodies[0].transform.TransformPoint(new Vector3(0,1,.6f)),Vector3.one*4);
            foreach(var b in bodies){bounds.Encapsulate(b.transform.TransformPoint(new Vector3(-1.5f,0,-1)));bounds.Encapsulate(b.transform.TransformPoint(new Vector3(1.5f,3,2)));}
            float half=view.fieldOfView*Mathf.Deg2Rad*.5f;half=Mathf.Min(half,Mathf.Atan(Mathf.Tan(half)*view.aspect));
            float distance=bounds.extents.magnitude/Mathf.Sin(half)*.85f;
            Vector3 direction=new Vector3(.35f,.8f,-1).normalized;
            position=bounds.center+direction*distance;rotation=Quaternion.LookRotation(bounds.center-position,Vector3.up);
        }
    }
}
