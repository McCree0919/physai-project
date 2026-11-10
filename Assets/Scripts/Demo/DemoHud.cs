using System.Linq;
using PhysicalAI.Climber;
using PhysicalAI.Structures;
using UnityEngine;

namespace PhysicalAI.Demo
{
    // Implemented by arena drivers that know their structure and climb history (DemoShowcase).
    public interface IDemoArenaStatus
    {
        string Title {get;}
        string Parameters {get;}
        string Status {get;}
        int Completions {get;}
        int Failures {get;}
    }

    // Implemented by the demo camera: which arena is followed and in which mode.
    public interface IDemoFocus
    {
        ClimberBody Focus {get;}
        int FocusIndex {get;}
        int Count {get;}
        string ModeName {get;}
    }

    // Read-only overlay; H toggles it, hidden by default.
    public sealed class DemoHud : MonoBehaviour
    {
        public KeyCode Toggle=KeyCode.H;
        public bool Visible;
        public string Heading="PHYSICAL AI CLIMBING  |  scripted controllers, unchanged body and physics";
        ClimberBody[] bodies;IDemoFocus view;IDemoArenaStatus[] arenas;
        GUIStyle style;
        void Start()
        {
            bodies=FindObjectsByType<ClimberBody>(FindObjectsSortMode.None).OrderBy(b=>b.name,System.StringComparer.Ordinal).ToArray();
            var behaviours=FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            view=behaviours.OfType<IDemoFocus>().FirstOrDefault();arenas=behaviours.OfType<IDemoArenaStatus>().ToArray();
        }
        void Update(){if(Input.GetKeyDown(Toggle))Visible=!Visible;}
        ClimberBody Focus()
        {
            if(view!=null&&view.Focus!=null)return view.Focus;
            return bodies!=null&&bodies.Length>0?bodies[0]:null;
        }
        void OnGUI()
        {
            if(!Visible)return;
            var body=Focus();if(body==null)return;
            if(style==null)style=new GUIStyle(GUI.skin.label) {richText=true};
            style.fontSize=Mathf.Max(13,Screen.height/48);
            var b=body.GetBodyState();var l=body.GetHandState(HandId.Left);var r=body.GetHandState(HandId.Right);
            var status=body.GetComponent<IDemoArenaStatus>();var box=body.GetComponent<BoxClimbSmokeController>();
            string Hand(string name,HandState h) => $"{name}: "+(h.Grabbed?"<color=#2ad94a>GRIPPED</color>":"FREE");
            var lines=new System.Collections.Generic.List<string> {"<b>"+Heading+"</b>"};
            if(view!=null)lines.Add($"Camera: {view.ModeName}   arena {view.FocusIndex+1}/{view.Count}");
            if(status!=null)
            {
                lines.Add($"<b>{status.Title}</b>   {status.Parameters}");
                lines.Add($"Status: {status.Status}   completed {status.Completions}   failed {status.Failures}");
            }
            else if(box!=null)lines.Add($"Box climb: stage {box.Stage}   {box.Elapsed:F1} s"+(box.Completed?"   ARRIVED":box.Failed?"   FAILED":""));
            lines.Add(Hand("<color=#3d74ff>Left hand</color>",l)+"     "+Hand("<color=#ff4040>Right hand</color>",r));
            lines.Add($"Torso height {b.Position.y:F2} m");
            if(arenas.Length>1)lines.Add($"All {arenas.Length} arenas: {arenas.Sum(a=>a.Completions)} climbs completed, {arenas.Sum(a=>a.Failures)} failed");
            lines.Add("<color=#aaaaaa>H hide HUD   C camera mode   arrow keys change arena</color>");
            string text=string.Join("\n",lines);
            var size=style.CalcSize(new GUIContent(text));
            GUI.color=new Color(0,0,0,.6f);GUI.DrawTexture(new Rect(10,10,size.x+24,size.y+16),Texture2D.whiteTexture);GUI.color=Color.white;
            GUI.Label(new Rect(22,18,size.x+4,size.y+4),text,style);
        }
    }
}
