using TMPro;
using UnityEngine;

namespace CDE2501.Wayfinding.UI
{
    public class ReassuranceManager : MonoBehaviour
    {
        [SerializeField] private TMP_Text reassuranceText;
        [SerializeField] private string onTrackMessage = "You are on the correct path.";
        [SerializeField] private string offTrackMessage = "Please re-center and follow the next arrow.";

        public void SetOnTrack()
        {
            SetMessage(onTrackMessage);
        }

        public void SetOffTrack()
        {
            SetMessage(offTrackMessage);
        }

        public void SetMessage(string message)
        {
            if (reassuranceText != null)
            {
                reassuranceText.text = message;
            }
        }
    }
}
