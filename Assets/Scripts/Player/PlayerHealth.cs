using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerHealth : MonoBehaviour
{
    public int   hp             = 3;
    public float invincibleTime = 1f;

    float          invincibleTimer = 0f;
    SpriteRenderer sr;

    void Awake() => sr = GetComponent<SpriteRenderer>();

    void Update()
    {
        if (invincibleTimer > 0f) invincibleTimer -= Time.deltaTime;
    }

    public void TakeDamage(int amount)
    {
        if (invincibleTimer > 0f) return;
        hp -= amount;
        invincibleTimer = invincibleTime;
        Debug.Log($"[Player] HP: {hp}");
        if (hp <= 0) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
