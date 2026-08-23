using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 — a clearly-named TEMPORARY helicopter placeholder built from primitives.
    /// Reads as a helicopter from the portrait camera (body, nose, tail, rotors). Responds to
    /// StoryCueEvents.EventCue: approach, insert/hover, depart. The architecture allows this to
    /// be replaced by a real helicopter model without rewriting the sequence system — the cue
    /// contract (EventCue with ids) stays the same.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HelicopterPlaceholder : MonoBehaviour
    {
        private Transform _body;
        private Vector3 _targetPos;
        private bool _moving;
        private float _rotorSpin;

        private void Awake()
        {
            BuildVisual();
            gameObject.SetActive(false);
            _targetPos = transform.position;
        }

        private void OnEnable() => StoryCueEvents.EventCue += OnEventCue;
        private void OnDisable() => StoryCueEvents.EventCue -= OnEventCue;

        private void OnEventCue(string cueId)
        {
            switch (cueId)
            {
                case "helicopter_approach":
                    gameObject.SetActive(true);
                    transform.position = new Vector3(-12f, 8f, -30f);
                    _targetPos = new Vector3(-3f, 5f, -8f);
                    _moving = true;
                    break;
                case "helicopter_insert":
                    _targetPos = new Vector3(-2f, 4f, 2f);
                    _moving = true;
                    break;
                case "helicopter_depart":
                    _targetPos = new Vector3(15f, 12f, -25f);
                    _moving = true;
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
                transform.position = Vector3.Lerp(transform.position, _targetPos, Time.deltaTime * 1.5f);
                if (Vector3.SqrMagnitude(transform.position - _targetPos) < 0.5f)
                    _moving = false;
            }
        }

        private void BuildVisual()
        {
            _body = new GameObject("HeliBody").transform;
            _body.SetParent(transform, false);

            // Fuselage
            var fus = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fus.name = "Fuselage";
            fus.transform.SetParent(_body, false);
            fus.transform.localScale = new Vector3(0.6f, 1.5f, 0.6f);
            fus.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Nose
            var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            nose.name = "Nose";
            nose.transform.SetParent(_body, false);
            nose.transform.localPosition = new Vector3(0f, 0f, 1.2f);
            nose.transform.localScale = new Vector3(0.5f, 0.4f, 0.5f);

            // Tail
            var tail = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            tail.name = "Tail";
            tail.transform.SetParent(_body, false);
            tail.transform.localPosition = new Vector3(0f, 0.2f, -1.8f);
            tail.transform.localScale = new Vector3(0.2f, 1.2f, 0.2f);
            tail.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Main rotor
            var rotor = new GameObject("MainRotor");
            rotor.transform.SetParent(_body, false);
            rotor.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            var blade1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade1.transform.SetParent(rotor.transform, false);
            blade1.transform.localScale = new Vector3(2.5f, 0.05f, 0.15f);
            var blade2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade2.transform.SetParent(rotor.transform, false);
            blade2.transform.localScale = new Vector3(0.15f, 0.05f, 2.5f);

            // Skids
            for (int i = 0; i < 2; i++)
            {
                var skid = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                skid.name = $"Skid_{i}";
                skid.transform.SetParent(_body, false);
                skid.transform.localPosition = new Vector3(i == 0 ? -0.5f : 0.5f, -0.7f, 0f);
                skid.transform.localScale = new Vector3(0.08f, 1.2f, 0.08f);
                skid.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }
    }
}
