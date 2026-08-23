using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 — a clearly-named TEMPORARY helicopter placeholder built from primitives.
    /// Responds to StoryCueEvents.EventCue: approach, insert/hover, depart.
    ///
    /// 1Z.1 QA fix #6: subscribes in Awake (NOT OnEnable) because Awake calls SetActive(false)
    /// which prevents OnEnable from firing. Also repositions helicopter coordinates for portrait
    /// gameplay visibility from the cinematic camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HelicopterPlaceholder : MonoBehaviour
    {
        // Authored positions visible from the cinematic camera at (0, 16, -16) looking +Z.
        private static readonly Vector3 ApproachStart = new Vector3(-10f, 7f, -25f);
        private static readonly Vector3 InsertionPoint = new Vector3(-2f, 4.5f, 0f);
        private static readonly Vector3 DepartEnd = new Vector3(14f, 11f, -20f);

        private Transform _body;
        private Vector3 _targetPos;
        private bool _moving;
        private float _rotorSpin;
        private bool _departed;

        private void Awake()
        {
            BuildVisual();

            // Start invisible but ACTIVE — the component must stay enabled so subscription works.
            // Hide by moving far away rather than SetActive(false) (which kills OnEnable).
            transform.position = new Vector3(0f, -500f, 0f);
            _targetPos = transform.position;

            // Subscribe in Awake — NOT OnEnable — because we keep the GameObject active but hidden.
            StoryCueEvents.EventCue += OnEventCue;
            Debug.Log("[STORY HELI] Created and subscribed to EventCue.");
        }

        private void OnDestroy()
        {
            StoryCueEvents.EventCue -= OnEventCue;
        }

        private void OnEventCue(string cueId)
        {
            switch (cueId)
            {
                case "helicopter_approach":
                    transform.position = ApproachStart;
                    _targetPos = InsertionPoint;
                    _moving = true;
                    _departed = false;
                    Debug.Log("[STORY HELI] Cue received: helicopter_approach — moving to insertion.");
                    break;
                case "helicopter_insert":
                    _targetPos = InsertionPoint;
                    _moving = true;
                    Debug.Log("[STORY HELI] Cue received: helicopter_insert — holding at insertion.");
                    break;
                case "helicopter_depart":
                    _targetPos = DepartEnd;
                    _moving = true;
                    _departed = true;
                    Debug.Log("[STORY HELI] Cue received: helicopter_depart — departing.");
                    break;
            }
        }

        private void Update()
        {
            _rotorSpin += Time.deltaTime * 1200f;

            if (_body != null)
            {
                Transform rotor = _body.Find("MainRotor");
                if (rotor != null) rotor.localRotation = Quaternion.Euler(0f, _rotorSpin, 0f);
            }

            if (_moving)
            {
                transform.position = Vector3.Lerp(transform.position, _targetPos, Time.deltaTime * 1.2f);

                // Face the direction of travel.
                Vector3 dir = _targetPos - transform.position;
                if (dir.sqrMagnitude > 0.1f)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(dir.normalized, Vector3.up), Time.deltaTime * 2f);
                }

                if (Vector3.SqrMagnitude(transform.position - _targetPos) < 1f)
                {
                    _moving = false;
                    if (_departed)
                    {
                        transform.position = new Vector3(0f, -500f, 0f); // hide after depart
                        Debug.Log("[STORY HELI] Departure complete — hidden.");
                    }
                }
            }
        }

        private void BuildVisual()
        {
            _body = new GameObject("HeliBody").transform;
            _body.SetParent(transform, false);

            Material bodyMat = CreateMat(new Color(0.25f, 0.28f, 0.32f, 1f)); // dark grey-green
            Material accentMat = CreateMat(new Color(0.6f, 0.15f, 0.1f, 1f));  // dark red accent
            Material rotorMat = CreateMat(new Color(0.1f, 0.1f, 0.1f, 0.7f));  // dark translucent

            // Fuselage (wider for portrait readability)
            AddPart(_body, PrimitiveType.Capsule, "Fuselage", new Vector3(0.8f, 1.8f, 0.7f),
                new Vector3(0f, 0f, 0.2f), Quaternion.Euler(90f, 0f, 0f), bodyMat);

            // Nose
            AddPart(_body, PrimitiveType.Sphere, "Nose", new Vector3(0.6f, 0.5f, 0.6f),
                new Vector3(0f, 0f, 1.6f), Quaternion.identity, bodyMat);

            // Cockpit glass
            AddPart(_body, PrimitiveType.Sphere, "Cockpit", new Vector3(0.4f, 0.3f, 0.4f),
                new Vector3(0f, 0.4f, 1.1f), Quaternion.identity, accentMat);

            // Tail boom
            AddPart(_body, PrimitiveType.Capsule, "Tail", new Vector3(0.25f, 1.5f, 0.25f),
                new Vector3(0f, 0.25f, -1.8f), Quaternion.Euler(90f, 0f, 0f), bodyMat);

            // Tail fin
            AddPart(_body, PrimitiveType.Cube, "TailFin", new Vector3(0.1f, 0.6f, 0.3f),
                new Vector3(0f, 0.7f, -2.5f), Quaternion.identity, bodyMat);

            // Main rotor
            var rotor = new GameObject("MainRotor");
            rotor.transform.SetParent(_body, false);
            rotor.transform.localPosition = new Vector3(0f, 0.9f, 0.2f);
            AddPart(rotor.transform, PrimitiveType.Cube, "Blade1", new Vector3(3f, 0.06f, 0.18f),
                Vector3.zero, Quaternion.identity, rotorMat);
            AddPart(rotor.transform, PrimitiveType.Cube, "Blade2", new Vector3(0.18f, 0.06f, 3f),
                Vector3.zero, Quaternion.identity, rotorMat);
            AddPart(rotor.transform, PrimitiveType.Cylinder, "Hub", new Vector3(0.15f, 0.3f, 0.15f),
                new Vector3(0f, 0f, 0f), Quaternion.identity, bodyMat);

            // Tail rotor
            var tailRotor = new GameObject("TailRotor");
            tailRotor.transform.SetParent(_body, false);
            tailRotor.transform.localPosition = new Vector3(0f, 0.5f, -2.8f);
            AddPart(tailRotor.transform, PrimitiveType.Cube, "TailBlade", new Vector3(0.06f, 1f, 0.1f),
                Vector3.zero, Quaternion.identity, rotorMat);

            // Skids
            for (int i = 0; i < 2; i++)
            {
                AddPart(_body, PrimitiveType.Cylinder, $"Skid_{i}",
                    new Vector3(0.1f, 1.4f, 0.1f),
                    new Vector3(i == 0 ? -0.6f : 0.6f, -0.9f, 0.2f),
                    Quaternion.Euler(90f, 0f, 0f), bodyMat);
            }

            // Overall scale for portrait readability
            _body.localScale = Vector3.one * 1.5f;
        }

        private static Material CreateMat(Color color)
        {
            // URP/Lit-adjacent: use a simple unlit material so it renders without shader setup.
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = color;
            return mat;
        }

        private static void AddPart(Transform parent, PrimitiveType type, string name,
            Vector3 scale, Vector3 pos, Quaternion rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            // Remove colliders — helicopter is purely visual.
            var col = go.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }
    }
}
