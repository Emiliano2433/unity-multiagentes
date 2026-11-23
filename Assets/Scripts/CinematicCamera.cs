using UnityEngine;

/// Cámara cinemática que orbita lentamente al barco.
/// Clic derecho + mover el ratón para girar, rueda para acercar. Tecla C alterna órbita automática.
public class CinematicCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 targetOffset = new Vector3(0, 6, 0);
    public float distance = 42f;
    public float minDistance = 8f, maxDistance = 120f;
    public float yaw = -35f;
    public float pitch = 12f;
    public float autoOrbitSpeed = 3f;   // grados por segundo
    public bool autoOrbit = true;
    public float bobAmount = 0.25f;

    void LateUpdate()
    {
        if (target == null) return;
        float dt = Time.deltaTime;

#if ENABLE_INPUT_SYSTEM
        var mouse = UnityEngine.InputSystem.Mouse.current;
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.cKey.wasPressedThisFrame && !ShowInput.Escribiendo) autoOrbit = !autoOrbit;
        if (mouse != null)
        {
            if (mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * 0.15f;
                pitch = Mathf.Clamp(pitch - d.y * 0.15f, 2f, 70f);
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance * (1f - Mathf.Sign(scroll) * 0.08f), minDistance, maxDistance);
        }
#endif
        if (autoOrbit) yaw += autoOrbitSpeed * dt;
        Place(Mathf.Sin(Time.time * 0.6f) * bobAmount);
    }

    /// Coloca la cámara de inmediato (también se usa desde el editor).
    public void Place(float bob = 0f)
    {
        if (target == null) return;
        Vector3 focus = target.position + targetOffset;
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
        Vector3 pos = focus - rot * Vector3.forward * distance;
        pos.y += bob;

        var ocean = OceanWaves.Instance;
        if (ocean != null) pos.y = Mathf.Max(pos.y, ocean.HeightAt(pos) + 1.2f);

        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(focus - pos);
    }
}
