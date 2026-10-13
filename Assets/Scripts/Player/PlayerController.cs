using UnityEngine;

// Управление игроком: вид сверху (top-down), 8 направлений, без гравитации
[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 4f;    // скорость в юнитах/с

    private Rigidbody2D   rb;
    private SpriteRenderer sr;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();

        // Вид сверху — гравитация не нужна
        rb.gravityScale = 0f;
        rb.constraints  = RigidbodyConstraints2D.FreezeRotation;
    }

    void Update()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        // Нормализуем чтобы диагональ не была быстрее
        Vector2 dir = new Vector2(h, v);
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        rb.linearVelocity = dir * moveSpeed;

        // Разворот спрайта по горизонтали
        if (h > 0)       sr.flipX = false;
        else if (h < 0)  sr.flipX = true;
    }
}
