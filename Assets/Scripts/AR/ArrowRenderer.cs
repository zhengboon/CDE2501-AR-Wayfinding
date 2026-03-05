using UnityEngine;

namespace CDE2501.Wayfinding.AR
{
    public class ArrowRenderer : MonoBehaviour
    {
        [SerializeField] private Transform arrowTransform;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float anchorDistance = 1.5f;
        [SerializeField] private float anchorHeightOffset = 0.2f;
        [SerializeField] private float rotationLerpSpeed = 6f;

        private Vector3? _indoorTarget;
        private float? _outdoorTargetBearing;

        public void SetIndoorTarget(Vector3 worldTarget)
        {
            _indoorTarget = worldTarget;
            _outdoorTargetBearing = null;
        }

        public void SetOutdoorTargetBearing(float bearingDegrees)
        {
            _outdoorTargetBearing = bearingDegrees;
            _indoorTarget = null;
        }

        private void Update()
        {
            if (arrowTransform == null || cameraTransform == null)
            {
                return;
            }

            Vector3 anchorPosition = cameraTransform.position + (cameraTransform.forward * anchorDistance);
            anchorPosition.y = cameraTransform.position.y + anchorHeightOffset;
            arrowTransform.position = anchorPosition;

            Quaternion targetRotation = arrowTransform.rotation;

            if (_indoorTarget.HasValue)
            {
                Vector3 lookDir = (_indoorTarget.Value - arrowTransform.position);
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    targetRotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
                }
            }
            else if (_outdoorTargetBearing.HasValue)
            {
                float yaw = _outdoorTargetBearing.Value;
                targetRotation = Quaternion.Euler(0f, yaw, 0f);
            }

            arrowTransform.rotation = Quaternion.Slerp(
                arrowTransform.rotation,
                targetRotation,
                rotationLerpSpeed * Time.deltaTime
            );
        }
    }
}
