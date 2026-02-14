using UnityEngine;

public class SfxPlayer : MonoBehaviour
{
    [Header("Audio Clips")]
    [SerializeField] private AudioClip diceClip;
    [SerializeField] private AudioClip stepClip;
    [SerializeField] private AudioClip captureClip;
    [SerializeField] private AudioClip winClip;
    [SerializeField] private AudioClip captureMoveClip;
    [SerializeField] private AudioClip yourTurnClip;
    [SerializeField] private AudioClip finishClip;
    [SerializeField] private AudioClip clockClip;       // ✅ Süre azalma sesi

    [Header("Settings")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioSource captureMoveSource; // ✅ Ayrı AudioSource (loop için)
    [SerializeField] private float volume = 1f;

    private bool _muted = false;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        // ✅ Capture movement için ayrı source yoksa oluştur
        if (captureMoveSource == null)
        {
            GameObject go = new GameObject("CaptureMoveSource");
            go.transform.SetParent(transform);
            captureMoveSource = go.AddComponent<AudioSource>();
            captureMoveSource.playOnAwake = false;
        }
    }

    public void SetMuted(bool muted)
    {
        _muted = muted;
    }

    public void PlayDice()
    {
        PlayOneShot(diceClip);
    }

    public void PlayStep()
    {
        PlayOneShot(stepClip);
    }

    public void PlayCapture()
    {
        PlayOneShot(captureClip);
    }

    public void PlayWin()
    {
        PlayOneShot(winClip);
    }

    /// <summary>
    /// ✅ Capture movement sesi başlat (loop veya tek seferlik)
    /// </summary>
    public void PlayCaptureMovement()
    {
        if (_muted || captureMoveSource == null || captureMoveClip == null)
            return;

        // ✅ Eğer zaten çalıyorsa durdur
        if (captureMoveSource.isPlaying)
            captureMoveSource.Stop();

        captureMoveSource.clip = captureMoveClip;
        captureMoveSource.volume = volume;
        captureMoveSource.loop = false; // ✅ Tek seferlik (veya true yapıp animasyon bitince StopCaptureMovement çağır)
        captureMoveSource.Play();
    }

    /// <summary>
    /// ✅ Capture movement sesini durdur (animasyon bitince)
    /// </summary>
    public void StopCaptureMovement()
    {
        if (captureMoveSource != null && captureMoveSource.isPlaying)
            captureMoveSource.Stop();
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (_muted) return;
        if (audioSource == null || clip == null) return;

        audioSource.PlayOneShot(clip, volume);
    }
    public void PlayYourTurn()
{
    PlayOneShot(yourTurnClip);
}

public void PlayFinish()
{
    PlayOneShot(finishClip);
}

public void PlayClock()
{
    PlayOneShot(clockClip);
}

}