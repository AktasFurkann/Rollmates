using System;
using System.Collections;
using System.Collections.Generic;
using LudoFriends.Presentation;
using UnityEngine;

namespace LudoFriends.Gameplay
{
    public class PawnMover : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float stepDuration = 0.15f;
        [SerializeField] private bool useSmoothLerp = true;

        [Header("Capture Animation")]
        [SerializeField] private float captureStepDuration = 0.08f; // ✅ Daha hızlı (capture için)

        [Header("Step SFX")]
        [SerializeField] private bool playStepEveryTile = false;
        [SerializeField] private float stepSfxMinInterval = 0.12f;
        private float _lastStepSfxTime = -999f;

        [Header("Audio (optional)")]
        [SerializeField] private SfxPlayer sfx;

        private readonly Dictionary<PawnView, Coroutine> _running = new Dictionary<PawnView, Coroutine>();

        public void SetStepDuration(float value)
        {
            stepDuration = Mathf.Clamp(value, 0.03f, 0.4f);
        }

        /// <summary>
        /// İleri doğru hareket (normal)
        /// </summary>
        public void MoveSteps(PawnView pawn, IReadOnlyList<Transform> path, int fromIndex, int steps, Action onComplete = null)
        {
            if (pawn == null || path == null || path.Count == 0) return;
            if (steps <= 0) return;

            StopMove(pawn);

            var co = StartCoroutine(CoMoveSteps(pawn, path, fromIndex, steps, stepDuration, onComplete));
            _running[pawn] = co;
        }

        /// <summary>
        /// ✅ YENİ: Geriye doğru hareket (capture için)
        /// </summary>
        public void MoveBackwardsToHome(PawnView pawn, IReadOnlyList<Transform> path, int currentIndex, int startIndex, Vector3 homePosition, Action onComplete = null)
{
    if (pawn == null || path == null || path.Count == 0) return;

    StopMove(pawn);

    // ✅ Capture movement sesini başlat
    if (sfx != null)
        sfx.PlayCaptureMovement();

    var co = StartCoroutine(CoMoveBackwardsToHome(pawn, path, currentIndex, startIndex, homePosition, () =>
    {
        // ✅ Animasyon bitti, sesi durdur
        if (sfx != null)
            sfx.StopCaptureMovement();

        // Callback çağır
        onComplete?.Invoke();
    }));

    _running[pawn] = co;
}


        public void StopMove(PawnView pawn)
        {
            if (pawn == null) return;

            if (_running.TryGetValue(pawn, out var co) && co != null)
                StopCoroutine(co);

            _running.Remove(pawn);
        }

        private void TryPlayCaptureSfx()
{
    if (sfx == null) return;

    // ✅ Özel capture movement sesi (SfxPlayer'a ekleyeceğiz)
    sfx.PlayCaptureMovement();
}

        /// <summary>
        /// İleri hareket coroutine
        /// </summary>
        private IEnumerator CoMoveSteps(PawnView pawn, IReadOnlyList<Transform> path, int fromIndex, int steps, float duration, Action onComplete)
        {
            int count = path.Count;
            int current = fromIndex;

            for (int i = 0; i < steps; i++)
            {
                current = (current + 1) % count;
                Vector3 target = path[current].position;

                TryPlayStepSfx();

                if (!useSmoothLerp)
                {
                    pawn.SetPosition(target);
                    yield return new WaitForSeconds(duration);
                }
                else
                {
                    Vector3 start = pawn.Rect.position;
                    float t = 0f;

                    while (t < 1f)
                    {
                        t += Time.deltaTime / Mathf.Max(0.0001f, duration);
                        pawn.Rect.position = Vector3.Lerp(start, target, t);
                        yield return null;
                    }

                    pawn.SetPosition(target);
                }
            }

            _running.Remove(pawn);
            onComplete?.Invoke();
        }

        /// <summary>
        /// ✅ Geriye doğru hareket coroutine (capture için)
        /// </summary>
        private IEnumerator CoMoveBackwardsToHome(PawnView pawn, IReadOnlyList<Transform> path, int currentIndex, int startIndex, Vector3 homePosition, Action onComplete)
{
    int count = path.Count;
    int current = currentIndex;

    // ✅ Geriye doğru
    while (current != startIndex)
    {
        current = (current - 1 + count) % count;

        Vector3 target = path[current].position;

        if (!useSmoothLerp)
        {
            pawn.SetPosition(target);
            yield return new WaitForSeconds(captureStepDuration);
        }
        else
        {
            Vector3 start = pawn.Rect.position;
            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.0001f, captureStepDuration);
                pawn.Rect.position = Vector3.Lerp(start, target, t);
                yield return null;
            }

            pawn.SetPosition(target);
        }
    }

    // ✅ Eve git
    if (useSmoothLerp)
    {
        Vector3 start = pawn.Rect.position;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, captureStepDuration);
            pawn.Rect.position = Vector3.Lerp(start, homePosition, t);
            yield return null;
        }
    }

    pawn.SetPosition(homePosition);

    _running.Remove(pawn);

    // ✅ Callback (ses durduracak)
    onComplete?.Invoke();
}

/// <summary>
/// Verilen pozisyon listesinde adım adım ilerle (main path, home lane, veya ikisi birlikte)
/// </summary>
public void MoveAlongPositions(PawnView pawn, List<Vector3> positions, Action onComplete = null)
{
    if (pawn == null || positions == null || positions.Count == 0)
    {
        onComplete?.Invoke();
        return;
    }

    StopMove(pawn);
    var co = StartCoroutine(CoMoveAlongPositions(pawn, positions, stepDuration, onComplete));
    _running[pawn] = co;
}

private IEnumerator CoMoveAlongPositions(PawnView pawn, List<Vector3> positions, float duration, Action onComplete)
{
    for (int i = 0; i < positions.Count; i++)
    {
        Vector3 target = positions[i];
        TryPlayStepSfx();

        if (!useSmoothLerp)
        {
            pawn.SetPosition(target);
            yield return new WaitForSeconds(duration);
        }
        else
        {
            Vector3 start = pawn.Rect.position;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.0001f, duration);
                pawn.Rect.position = Vector3.Lerp(start, target, t);
                yield return null;
            }
            pawn.SetPosition(target);
        }
    }

    _running.Remove(pawn);
    onComplete?.Invoke();
}

        private void TryPlayStepSfx()
        {
            if (sfx == null) return;

            if (playStepEveryTile)
            {
                sfx.PlayStep();
                return;
            }

            if (Time.time - _lastStepSfxTime < stepSfxMinInterval)
                return;

            _lastStepSfxTime = Time.time;
            sfx.PlayStep();
        }
    }

}