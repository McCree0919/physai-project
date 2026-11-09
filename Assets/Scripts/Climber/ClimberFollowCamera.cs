using UnityEngine;
namespace PhysicalAI.Climber
{
    [RequireComponent(typeof(Camera))]
    public sealed class ClimberFollowCamera : MonoBehaviour
    {
        [SerializeField] ClimberManualController controls;
        Camera view;
        bool positioned;
        public void Configure(ClimberManualController value) {controls=value;}
        void Awake() {view=GetComponent<Camera>();view.fieldOfView=45;view.nearClipPlane=.05f;view.farClipPlane=200;}
        void LateUpdate() {FrameTarget(Time.deltaTime);}
        internal void FrameTarget(float dt)
        {
            if(controls==null||controls.FocusBody==null)return;
            if(view==null)view=GetComponent<Camera>();
            var body=controls.FocusBody;
            // Include the whole teaching wall/platform, plus the moving body and both hands.
            Bounds bounds=new Bounds(new Vector3(0,1.4f,.6f),new Vector3(2.2f,2.8f,2));
            bounds.Encapsulate(body.GetBodyState().Position+Vector3.up*.4f);
            bounds.Encapsulate(body.GetBodyState().Position-Vector3.up*.4f);
            bounds.Encapsulate(body.GetHandState(HandId.Left).Position);
            bounds.Encapsulate(body.GetHandState(HandId.Right).Position);
            view.rect=new Rect(.34f,0,.66f,1);
            float halfVertical=view.fieldOfView*Mathf.Deg2Rad*.5f;
            float halfHorizontal=Mathf.Atan(Mathf.Tan(halfVertical)*view.aspect);
            float distance=bounds.extents.magnitude/Mathf.Sin(Mathf.Min(halfVertical,halfHorizontal))*1.15f;
            Vector3 center=body.transform.TransformPoint(bounds.center);
            Vector3 desired=center+body.transform.TransformDirection(new Vector3(2.7f,1.8f,-5.5f).normalized)*distance;
            transform.position=positioned?Vector3.Lerp(transform.position,desired,1-Mathf.Exp(-6*dt)):desired;
            transform.rotation=Quaternion.LookRotation(center-transform.position,body.transform.up);
            positioned=true;
        }
    }
}
