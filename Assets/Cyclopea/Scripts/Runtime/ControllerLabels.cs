using UnityEngine;

namespace Cyclopea
{
    /// <summary>
    /// Which button does what, on the controllers themselves: when controllers are picked up instead of hands,
    /// a label floats over each one for a few seconds. Ported from the same author's Worldeck.
    /// </summary>
    public sealed class ControllerLabels : MonoBehaviour
    {
        public Transform Left, Right, Head;
        public Material Panel;
        public float ShowSeconds = 12f;

        public const string LeftText = "Trigger: catch or choose\nMenu button: pause";
        public const string RightText = "Trigger or A: catch or choose\nPut them down to use hands";

        TextMesh left, right;
        float until = -1f;
        bool was;

        public bool Showing => left != null && left.gameObject.activeSelf;

        void Start()
        {
            left = Make("Controller label L", LeftText);
            right = Make("Controller label R", RightText);
        }

        TextMesh Make(string name, string text)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.fontSize = 48;
            tm.characterSize = 0.0045f;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.97f, 0.92f, 0.95f);
            tm.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            if (Panel != null) go.AddComponent<TextPanel>().Material = Panel;
            go.SetActive(false);
            return tm;
        }

        /// <summary>Show the labels again (for example from a help screen).</summary>
        public void ShowNow(float seconds = 10f) => until = Time.unscaledTime + seconds;

        void LateUpdate()
        {
            if (left == null) return;
            bool any = ControllerSelector.InUse;
            if (any && !was)
            {
                until = Mathf.Max(until, Time.unscaledTime + ShowSeconds);
                Milestones.Mark("controllers", "labels shown");
            }
            was = any;
            bool show = any && Time.unscaledTime < until;
            Place(left, Left, show);
            Place(right, Right, show);
        }

        void Place(TextMesh label, Transform anchor, bool show)
        {
            bool on = show && anchor != null && anchor.gameObject.activeInHierarchy;
            if (label.gameObject.activeSelf != on) label.gameObject.SetActive(on);
            if (!on) return;
            Vector3 p = anchor.position + Vector3.up * 0.07f;
            label.transform.position = p;
            if (Head != null) label.transform.rotation = Quaternion.LookRotation(p - Head.position, Vector3.up);
        }
    }
}
