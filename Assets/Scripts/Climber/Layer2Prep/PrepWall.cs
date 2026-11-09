using UnityEngine;

namespace PhysicalAI.Climber.Layer2Prep
{
    // New environment geometry only. The existing factory retains every body parameter.
    public sealed class PrepWall
    {
        public readonly ClimberBody Body;
        public readonly ClimberGoal Goal;
        public float HeightGap {get;private set;}
        public float LateralGap {get;private set;}
        public float Top => 1f+4*HeightGap;
        readonly PhysicsMaterial material;
        readonly System.Collections.Generic.List<GameObject> geometry=new System.Collections.Generic.List<GameObject>();
        public PrepWall(int index,PhysicsMaterial value,float yaw)
        {
            material=value;
            Body=ClimberArenaFactory.Create("PrepArena_"+index,new Vector3((index%8)*7,0,(index/8)*7),value,false);
            Goal=Body.GetComponent<ClimberGoal>();
            foreach(Transform child in Body.transform)
                if(child.name.StartsWith("Rung")||child.name=="TopPlatform"||child.name=="Wall")geometry.Add(child.gameObject);
            Body.transform.rotation=Quaternion.Euler(0,yaw,0);
            Physics.SyncTransforms();
        }
        public void Configure(float heightGap,float lateralGap)
        {
            HeightGap=heightGap;LateralGap=lateralGap;
            foreach(var item in geometry){item.SetActive(false);Object.Destroy(item);}geometry.Clear();
            for(int i=0;i<4;i++)
                Add("PrepRung"+i,new Vector3(i%2==0?-lateralGap*.5f:lateralGap*.5f,.95f+i*heightGap,-.075f),new Vector3(.5f,.1f,.25f),true);
            Add("PrepTop",new Vector3(0,Top-.1f,.75f),new Vector3(2,.2f,1.5f),true);
            Add("PrepWall",new Vector3(0,(Top-.2f)*.5f,.45f),new Vector3(2,Top-.2f,.15f),false);
            Goal.Center=new Vector3(0,Top+.6f,.6f);Goal.Size=new Vector3(.8f,.4f,.6f);
        }
        void Add(string name,Vector3 p,Vector3 size,bool grab)
        {geometry.Add(ClimberArenaFactory.Box(name,Body.transform,p,size,material,grab).gameObject);}
        public void Reset(float spawnX)
        {
            Vector3 shift=Vector3.right*spawnX;
            Body.ResetBody(new BodyReset {Torso=new Vector3(0,.4f,-.4f)+shift,
                LeftHand=new Vector3(-.3f,1.15f,-.05f)+shift,RightHand=new Vector3(.3f,.95f,-.05f)+shift,Rotation=Quaternion.identity});
        }
    }
}
