using UnityEngine;

/// Hace que el barco cabecee, se balancee y suba/baje con las olas.
public class ShipBuoyancy : MonoBehaviour
{
    public float halfLength = 12f;
    public float halfBeam = 4f;
    [Tooltip("Cuánto sigue el barco la altura de las olas (0-1)")]
    public float heaveFollow = 0.8f;
    public float rotationFollow = 0.7f;
    public float smoothing = 2.5f;
    [Tooltip("Pequeño vaivén extra de proa (grados)")]
    public float yawSway = 1.2f;

    Vector3 _basePos;
    Quaternion _baseRot;

    void Start()
    {
        _basePos = transform.position;
        _baseRot = transform.rotation;
    }

    void LateUpdate()
    {
        var ocean = OceanWaves.Instance;
        if (ocean == null) return;

        Vector3 fwd = _baseRot * Vector3.right;     // la proa del Going Merry mira a +X local
        Vector3 side = _baseRot * Vector3.forward;
        float oy = ocean.transform.position.y;

        float bow  = ocean.HeightAt(_basePos + fwd * halfLength) - oy;
        float stern = ocean.HeightAt(_basePos - fwd * halfLength) - oy;
        float port = ocean.HeightAt(_basePos + side * halfBeam) - oy;
        float star = ocean.HeightAt(_basePos - side * halfBeam) - oy;
        float mid  = ocean.HeightAt(_basePos) - oy;

        float heave = (bow + stern + port + star + mid) / 5f * heaveFollow;
        float pitch = Mathf.Atan2(bow - stern, halfLength * 2f) * Mathf.Rad2Deg * rotationFollow;
        float roll  = Mathf.Atan2(port - star, halfBeam * 2f) * Mathf.Rad2Deg * rotationFollow;
        float yaw   = Mathf.Sin(Time.time * 0.23f) * yawSway;

        Vector3 targetPos = _basePos + Vector3.up * heave;
        Quaternion targetRot = _baseRot * Quaternion.Euler(-roll, yaw, pitch);

        float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, k);
    }
}
