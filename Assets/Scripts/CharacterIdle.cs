using UnityEngine;

/// Reproduce en bucle la animación importada del personaje (Animation legacy de glTFast).
public class CharacterIdle : MonoBehaviour
{
    public float speed = 1f;

    void Start()
    {
        var anim = GetComponentInChildren<Animation>();
        if (anim == null) return;
        AnimationState first = null;
        foreach (AnimationState s in anim)
        {
            s.wrapMode = WrapMode.Loop;
            s.speed = speed;
            if (first == null) first = s;
        }
        if (first != null) anim.Play(first.name);
    }
}
