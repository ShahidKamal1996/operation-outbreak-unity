using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 QA fix #11 — the exterior helicopter for Mission 01's cinematic.
    ///
    /// It now drives the REAL imported Copter_2 model (loaded from Resources) for every exterior
    /// shot, replacing the old primitive placeholder visually. The flight choreography
    /// (approach -> insert/hover -> depart) and the cue subscription are preserved exactly, so the
    /// opening sequence's fade-gated exterior -> interior -> exterior flow is unchanged.
    ///
    /// Rotor note: Copter_2 is a single baked OBJ mesh, so its rotor is not a separate object that
    /// can be spun. A lightweight translucent "MainRotor" disc is overlaid on top and spun fast to
    /// give the motion cue. (If a future Copter_2 variant exposes a named rotor object, it is also
    /// spun.) A primitive fallback is kept so the cinematic can never break if the asset is missing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HelicopterPlaceholder : MonoBehaviour
    {
        private const string ModelResourcesPath = "Helicopter/Model/Copter_2";
        private const float TargetLengthMeters = 6f;   // cinematic size for the real model
        private const float SkidClearance = 0.1f;

        // Authored positions visible from the cinematic exterior camera.
        private static readonly Vector3 ApproachStart = new Vector3(-10f, 7f, -25f);
        private static readonly Vector3 InsertionPoint = new Vector3(-2f, 4.5f, 0f);
        private static readonly Vector3 DepartEnd = new Vector3(14f, 11f, -20f);

        private Transform _body;
        private Vector3 _targetPos;
        private bool _moving;
        private float _rotorSpin;
        private bool _departed;
        private bool _usesRealModel;

        /// <summary>True when the real Copter_2 model is the active visual.</summary>
        public bool UsesRealModel => _usesRealModel;

        private void Awake()
        {
            BuildVisual();

            // Start invisible but ACTIVE — the component must stay enabled so the cue subscription
            // works. Hide by moving far away rather than SetActive(false) (which kills OnEnable).
            transform.position = new Vector3(0f, -500f, 0f);
            _targetPos = transform.position;

            // Subscribe in Awake — NOT OnEnable — because the GameObject stays active but hidden.
            StoryCueEvents.EventCue += OnEventCue;
            Debug.Log("[STORY HELI] Created and subscribed to EventCue (real model=" + _usesRealModel + ").");
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

        /// <summary>Instantly hides the helicopter (skip / handoff safety). Idempotent.</summary>
        public void HideNow()
        {
            _moving = false;
            _departed = false;
            transform.position = new Vector3(0f, -500f, 0f);
            _targetPos = transform.position;
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

        // ===================================================================== visual

        private void BuildVisual()
        {
            GameObject model = Resources.Load<GameObject>(ModelResourcesPath);
            if (model != null)
            {
                _body = Object.Instantiate(model, transform, false).transform;
                _body.name = "HeliBody";
                _body.localPosition = Vector3.zero;
                _body.localRotation = Quaternion.identity;

                // Scale the real model to a cinematic size and sit its skids near the origin.
                Bounds b = CombineBounds(_body);
                float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (maxDim > 0.01f)
                {
                    _body.localScale = Vector3.one * (TargetLengthMeters / maxDim);
                    Bounds b2 = CombineBounds(_body);
                    _body.localPosition = new Vector3(0f, -b2.min.y + SkidClearance, 0f);
                }

                BuildRotorOverlay(_body);
                _usesRealModel = true;
                Debug.Log("[STORY HELI] Real Copter_2 model loaded for exterior shots (scaled to ~"
                          + TargetLengthMeters + "m).");
            }
            else
            {
                BuildPrimitiveFallback();
                Debug.LogWarning("[STORY HELI] Copter_2 model not found in Resources — using primitive fallback.");
            }
        }

        /// <summary>
        /// Overlays a fast-spinning translucent disc above the model as a rotor motion cue (the real
        /// OBJ bakes the rotor into its mesh, so it cannot be spun directly).
        /// </summary>
        private void BuildRotorOverlay(Transform body)
        {
            Bounds b = CombineBounds(body);
            var rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rotor.name = "MainRotor";
            rotor.transform.SetParent(body, false);
            float radius = Mathf.Max(b.size.x, b.size.z) * 0.5f;
            rotor.transform.localScale = new Vector3(Mathf.Max(0.5f, radius), 0.02f, Mathf.Max(0.5f, radius));
            rotor.transform.localRotation = Quaternion.identity;
            rotor.transform.localPosition = new Vector3(0f, b.max.y - body.position.y + 0.05f, 0f);
            var col = rotor.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            var mr = rotor.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = CreateMat(new Color(0.05f, 0.05f, 0.05f, 0.45f));
        }

        private static Bounds CombineBounds(Transform root)
        {
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // ===================================================================== primitive fallback

        private void BuildPrimitiveFallback()
        {
            _body = new GameObject("HeliBody").transform;
            _body.SetParent(transform, false);

            Material bodyMat = CreateMat(new Color(0.25f, 0.28f, 0.32f, 1f));
            Material accentMat = CreateMat(new Color(0.6f, 0.15f, 0.1f, 1f));
            Material rotorMat = CreateMat(new Color(0.1f, 0.1f, 0.1f, 0.7f));

            AddPart(_body, PrimitiveType.Capsule, "Fuselage", new Vector3(0.8f, 1.8f, 0.7f),
                new Vector3(0f, 0f, 0.2f), Quaternion.Euler(90f, 0f, 0f), bodyMat);
            AddPart(_body, PrimitiveType.Sphere, "Nose", new Vector3(0.6f, 0.5f, 0.6f),
                new Vector3(0f, 0f, 1.6f), Quaternion.identity, bodyMat);
            AddPart(_body, PrimitiveType.Sphere, "Cockpit", new Vector3(0.4f, 0.3f, 0.4f),
                new Vector3(0f, 0.4f, 1.1f), Quaternion.identity, accentMat);
            AddPart(_body, PrimitiveType.Capsule, "Tail", new Vector3(0.25f, 1.5f, 0.25f),
                new Vector3(0f, 0.25f, -1.8f), Quaternion.Euler(90f, 0f, 0f), bodyMat);
            AddPart(_body, PrimitiveType.Cube, "TailFin", new Vector3(0.1f, 0.6f, 0.3f),
                new Vector3(0f, 0.7f, -2.5f), Quaternion.identity, bodyMat);

            var rotor = new GameObject("MainRotor");
            rotor.transform.SetParent(_body, false);
            rotor.transform.localPosition = new Vector3(0f, 0.9f, 0.2f);
            AddPart(rotor.transform, PrimitiveType.Cube, "Blade1", new Vector3(3f, 0.06f, 0.18f),
                Vector3.zero, Quaternion.identity, rotorMat);
            AddPart(rotor.transform, PrimitiveType.Cube, "Blade2", new Vector3(0.18f, 0.06f, 3f),
                Vector3.zero, Quaternion.identity, rotorMat);

            _body.localScale = Vector3.one * 1.5f;
        }

        private static Material CreateMat(Color color)
        {
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
            var col = go.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }
    }
}
