using UnityEngine;
using UnityEngine.InputSystem;

namespace Kit1DigitalTwin
{
    public sealed class OrbitCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        [SerializeField] private float orbitSpeed = 0.18f;
        [SerializeField] private float panSpeed = 0.0025f;
        [SerializeField] private float zoomSpeed = 0.35f;
        [SerializeField] private float minimumDistance = 0.25f;
        [SerializeField] private float maximumDistance = 8f;

        private float distance;
        private float yaw;
        private float pitch = 25f;

        private void Start()
        {
            if (Target == null)
            {
                GameObject targetObject = GameObject.Find("CameraTarget");
                Target = targetObject != null ? targetObject.transform : null;
            }

            if (Target == null)
            {
                return;
            }

            Vector3 offset = transform.position - Target.position;
            distance = Mathf.Clamp(offset.magnitude, minimumDistance, maximumDistance);
            yaw = transform.eulerAngles.y;
            pitch = NormalizeAngle(transform.eulerAngles.x);
        }

        private void LateUpdate()
        {
            if (Target == null || Mouse.current == null)
            {
                return;
            }

            Vector2 pointerPosition = Mouse.current.position.ReadValue();
            if (Kit1DigitalTwin.Hmi.Kit1EngineeringDashboard.ContainsScreenPoint(pointerPosition) ||
                Kit1DigitalTwin.Hmi.Kit2EngineeringDashboard.ContainsScreenPoint(pointerPosition))
            {
                return;
            }

            bool shiftHeld = Keyboard.current != null &&
                (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
            bool isPanning = Mouse.current.middleButton.isPressed ||
                (shiftHeld && Mouse.current.rightButton.isPressed);

            if (isPanning)
            {
                Vector2 delta = Mouse.current.delta.ReadValue();
                Quaternion viewRotation = Quaternion.Euler(pitch, yaw, 0f);
                float worldUnitsPerPixel = panSpeed * Mathf.Max(distance, minimumDistance);
                Vector3 pan = viewRotation * new Vector3(-delta.x, -delta.y, 0f);
                Target.position += pan * worldUnitsPerPixel;
            }
            else if (Mouse.current.rightButton.isPressed)
            {
                Vector2 delta = Mouse.current.delta.ReadValue();
                yaw += delta.x * orbitSpeed;
                pitch = Mathf.Clamp(pitch - delta.y * orbitSpeed, -10f, 80f);
            }

            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > Mathf.Epsilon)
            {
                // Windows wheels commonly report 120 per notch while some devices
                // report 1. Normalize both forms and preserve smooth trackpad input.
                float scrollSteps = Mathf.Abs(scroll) > 10f ? scroll / 120f : scroll;
                float speedMultiplier = shiftHeld ? 2.5f : 1f;
                distance = Mathf.Clamp(
                    distance * Mathf.Exp(-scrollSteps * zoomSpeed * speedMultiplier),
                    minimumDistance,
                    maximumDistance);
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(
                Target.position - rotation * Vector3.forward * distance,
                rotation);
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
