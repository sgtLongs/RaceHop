using UnityEngine;

/// <summary>
/// Plays an intro clip once, then cross-hands to a loop clip with sample-accurate timing.
/// Uses TWO AudioSources so both clips stay loaded & scheduled from the start.
/// </summary>
public class MusicManager : MonoBehaviour
{
	[Header("Audio Clips")]
	public AudioClip introClip;   // one-shot intro
	public AudioClip loopClip;    // looping bed

	[Header("Options")]
	[Range(0f, 1f)] public float volume = 1f;
	public bool persistAcrossScenes = true;

	AudioSource introSource;
	AudioSource loopSource;

	void Awake()
	{
		// Optional singleton
		if (persistAcrossScenes)
		{
			if (FindObjectsOfType<MusicManager>().Length > 1) { Destroy(gameObject); return; }
			DontDestroyOnLoad(gameObject);
		}

		// Create / configure the two sources
		introSource = gameObject.AddComponent<AudioSource>();
		introSource.clip = introClip;
		introSource.loop = false;
		introSource.volume = volume;
		introSource.playOnAwake = false;

		loopSource = gameObject.AddComponent<AudioSource>();
		loopSource.clip = loopClip;
		loopSource.loop = true;
		loopSource.volume = volume;
		loopSource.playOnAwake = false;

		StartMusic();
	}

	void StartMusic()
	{
		if (introClip == null || loopClip == null)
		{
			Debug.LogWarning("MusicManager is missing an AudioClip reference.");
			return;
		}

		// Make absolutely sure the loop clip is in memory & decoded
		if (!loopClip.preloadAudioData && !loopClip.loadInBackground)
			loopClip.LoadAudioData();  // synchronous but quick for small/medium files

		double dspStart = AudioSettings.dspTime + 0.1;      // tiny safety offset
		double loopAt = dspStart + introClip.length;      // exact hand-off time

		introSource.PlayScheduled(dspStart);                // start intro
		loopSource.PlayScheduled(loopAt);                   // queue loop

		// Optional: stop intro source exactly at its end so voices don't overlap
		introSource.SetScheduledEndTime(loopAt);
	}

	/* ---------- Helpers ---------- */

	public void StopMusic(float fadeSeconds = 0f)
	{
		if (fadeSeconds <= 0f)
		{
			introSource.Stop();
			loopSource.Stop();
			return;
		}
		StartCoroutine(FadeOut(fadeSeconds));
	}
	System.Collections.IEnumerator FadeOut(float t)
	{
		float startVol = volume;
		for (float e = 0; e < t; e += Time.deltaTime)
		{
			float v = Mathf.Lerp(startVol, 0f, e / t);
			introSource.volume = loopSource.volume = v;
			yield return null;
		}
		introSource.Stop();
		loopSource.Stop();
		introSource.volume = loopSource.volume = startVol;
	}
}
