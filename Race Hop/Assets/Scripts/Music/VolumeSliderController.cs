using UnityEngine;
using UnityEngine.UI;     // Slider
using TMPro;              // TextMeshProUGUI

/// <summary>
/// Links a UI Slider to the master volume for every AudioSource in the scene.
/// </summary>
public class VolumeSliderController : MonoBehaviour
{
	[Header("UI References")]
	[SerializeField] private Slider volumeSlider;     // Slider that ranges 0-1
	[SerializeField] private TMP_Text percentLabel;   // Optional: shows “42 %”

	private void Awake()
	{
		if (volumeSlider == null)
		{
			Debug.LogError($"{nameof(VolumeSliderController)}: No Slider assigned!");
			enabled = false;
			return;
		}

		// Make sure slider starts at the current volume
		volumeSlider.value = AudioListener.volume;

		// React whenever the slider moves
		volumeSlider.onValueChanged.AddListener(SetMasterVolume);

		// Update the label once at startup
		SetMasterVolume(volumeSlider.value);
	}

	/// <summary>
	/// Called automatically when the slider value changes.
	/// </summary>
	private void SetMasterVolume(float value)
	{
		// Clamp just in case, then set global volume
		value = Mathf.Clamp01(value);
		AudioListener.volume = value;

		// Show “0 % … 100 %” if a label is wired up
		if (percentLabel != null)
			percentLabel.text = Mathf.RoundToInt(value * 100f) + " %";
	}

	private void OnDestroy()
	{
		// Clean up the listener to avoid memory leaks in play mode
		if (volumeSlider != null)
			volumeSlider.onValueChanged.RemoveListener(SetMasterVolume);
	}
}
