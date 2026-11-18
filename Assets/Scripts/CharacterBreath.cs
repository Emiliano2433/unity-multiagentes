using UnityEngine;

/// Respiración sutil para personajes sin esqueleto: escala vertical leve desde el asiento.
public class CharacterBreath : MonoBehaviour
{
    public float amount = 0.009f;
    public float speed = 1.6f;
    public float phase;
    Vector3 _base;

    void Start() { _base = transform.localScale; }

    void Update()
    {
        float s = Mathf.Sin(Time.time * speed + phase);
        transform.localScale = new Vector3(_base.x * (1 - s * amount * 0.35f),
                                           _base.y * (1 + s * amount),
                                           _base.z * (1 - s * amount * 0.35f));
    }
}
