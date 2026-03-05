using System;
using CDE2501.Wayfinding.Routing;
using UnityEngine;

namespace CDE2501.Wayfinding.Elevation
{
    public enum PromptPolicy
    {
        Always = 0,
        Smart = 1,
        ManualOnly = 2
    }

    public class LevelManager : MonoBehaviour
    {
        [SerializeField] private PromptPolicy promptPolicy = PromptPolicy.Smart;
        [SerializeField] private ElevationLevel currentLevel = ElevationLevel.Deck;
        [SerializeField, Range(0f, 1f)] private float levelConfidence = 0.8f;
        [SerializeField, Range(0f, 1f)] private float lowConfidenceThreshold = 0.55f;

        private int _offRouteCount;

        public event Action<ElevationLevel> OnLevelChanged;
        public event Action<bool> OnPromptVisibilityChanged;

        public PromptPolicy CurrentPromptPolicy
        {
            get => promptPolicy;
            set => promptPolicy = value;
        }

        public ElevationLevel CurrentLevel => currentLevel;

        public void ConfirmLevel(ElevationLevel selected)
        {
            currentLevel = selected;
            levelConfidence = 1f;
            _offRouteCount = 0;
            OnLevelChanged?.Invoke(currentLevel);
            OnPromptVisibilityChanged?.Invoke(false);
        }

        public void SetLevelConfidence(float confidence)
        {
            levelConfidence = Mathf.Clamp01(confidence);
        }

        public void NotifyNearTransitionConnector()
        {
            TryPrompt(forceInAlways: true);
        }

        public void NotifyOffRoute()
        {
            _offRouteCount++;
            if (_offRouteCount >= 2)
            {
                SetLevelConfidence(Mathf.Min(levelConfidence, 0.4f));
                TryPrompt(forceInAlways: false);
            }
        }

        public void EvaluatePromptOnRouteStart()
        {
            TryPrompt(forceInAlways: true);
        }

        private void TryPrompt(bool forceInAlways)
        {
            if (promptPolicy == PromptPolicy.ManualOnly)
            {
                OnPromptVisibilityChanged?.Invoke(false);
                return;
            }

            if (promptPolicy == PromptPolicy.Always && forceInAlways)
            {
                OnPromptVisibilityChanged?.Invoke(true);
                return;
            }

            if (promptPolicy == PromptPolicy.Smart)
            {
                bool shouldPrompt = levelConfidence < lowConfidenceThreshold || _offRouteCount >= 2;
                OnPromptVisibilityChanged?.Invoke(shouldPrompt);
            }
        }
    }
}
