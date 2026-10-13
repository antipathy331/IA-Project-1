using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
public class EnemyBasic : MonoBehaviour
{
    public enum State { Idle, Patrol, Chase, Attack, Search }
    public State currentState = State.Idle;

    public float moveSpeed      = 2.5f;
    public float detectRange    = 5f;
    public float attackRange    = 1.2f;
    public float loseRange      = 8f;
    public int   damage         = 1;
    public float attackCooldown = 1.5f;
    public float searchTime     = 3f;
    public float idleWaitTime   = 2f;
    public Transform[] patrolPoints;
    public LayerMask   obstacleLayer;

    Rigidbody2D    rb;
    SpriteRenderer sr;
    Transform      player;
    int     patrolIndex   = 0;
    float   stateTimer    = 0f;
    float   attackTimer   = 0f;
    Vector2 facingDir     = Vector2.right;
    Vector2 lastKnownPos;

    // для логирования: не спамить одно и то же каждый кадр
    bool wasBlockedLastFrame = false;
    bool arrivedAtSearch     = false;

    string Tag => $"[{gameObject.name}]";

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        rb.gravityScale = 0f;
        rb.constraints  = RigidbodyConstraints2D.FreezeRotation;
    }

    void Start()
    {
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) player = go.transform;
        ChangeState(State.Idle, "старт");
    }

    void Update()
    {
        if (attackTimer > 0f) attackTimer -= Time.deltaTime;
        switch (currentState)
        {
            case State.Idle:   UpdateIdle();   break;
            case State.Patrol: UpdatePatrol(); break;
            case State.Chase:  UpdateChase();  break;
            case State.Attack: UpdateAttack(); break;
            case State.Search: UpdateSearch(); break;
        }
    }

    // Все переходы только через этот метод — выводит строку в консоль
    void ChangeState(State next, string reason)
    {
        Debug.Log($"{Tag} {currentState} -> {next} ({reason})");
        currentState = next;
        stateTimer   = 0f;
        arrivedAtSearch = false;
        if (next == State.Idle || next == State.Search)
            rb.linearVelocity = Vector2.zero;
    }

    void UpdateIdle()
    {
        rb.linearVelocity = Vector2.zero;
        stateTimer += Time.deltaTime;
        if (CanSeePlayer(logBlocked: true)) { ChangeState(State.Chase, "игрок обнаружен"); return; }
        if (stateTimer >= idleWaitTime)       ChangeState(State.Patrol, "ожидание истекло");
    }

    void UpdatePatrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) { ChangeState(State.Idle, "нет точек"); return; }
        if (CanSeePlayer(logBlocked: true)) { ChangeState(State.Chase, "игрок обнаружен"); return; }

        var target = patrolPoints[patrolIndex];
        Move8Dir(target.position);

        if (Vector2.Distance(transform.position, target.position) < 0.15f)
        {
            int next = (patrolIndex + 1) % patrolPoints.Length;
            Debug.Log($"{Tag} достиг точки {patrolIndex}, идёт к точке {next}");
            patrolIndex = next;
        }
    }

    void UpdateChase()
    {
        float dist = Vector2.Distance(transform.position, player.position);
        lastKnownPos = player.position;

        if (dist <= attackRange)                 { ChangeState(State.Attack, "игрок в радиусе атаки"); return; }
        if (dist > loseRange || !CanSeePlayer())
        {
            Debug.Log($"{Tag} потерял игрока, последняя позиция ({lastKnownPos.x:F1}, {lastKnownPos.y:F1})");
            ChangeState(State.Search, "игрок потерян");
            return;
        }
        Move8Dir(player.position);
    }

    void UpdateAttack()
    {
        rb.linearVelocity = Vector2.zero;
        if (Vector2.Distance(transform.position, player.position) > attackRange)
        { ChangeState(State.Chase, "игрок вышел из зоны атаки"); return; }
        if (attackTimer <= 0f) { attackTimer = attackCooldown; DoAttack(); }
    }

    void UpdateSearch()
    {
        if (CanSeePlayer(logBlocked: false)) { ChangeState(State.Chase, "игрок обнаружен в поиске"); return; }

        if (Vector2.Distance(transform.position, lastKnownPos) > 0.2f)
        {
            Move8Dir(lastKnownPos);
        }
        else
        {
            if (!arrivedAtSearch)
            {
                arrivedAtSearch = true;
                Debug.Log($"{Tag} пришёл на место, осматривается {searchTime:F1} с");
            }
            rb.linearVelocity = Vector2.zero;
            stateTimer += Time.deltaTime;
            if (stateTimer >= searchTime)
            {
                Debug.Log($"{Tag} поиск закончен, игрок не найден");
                ChangeState(State.Patrol, "поиск истёк");
            }
        }
    }

    void DoAttack()
    {
        var ph = player.GetComponent<PlayerHealth>();
        ph?.TakeDamage(damage);
        int hpLeft = ph != null ? ph.hp : -1;
        Debug.Log($"{Tag} Атака, урон {damage}, у игрока осталось hp {hpLeft}");
        StartCoroutine(FlashRed());
    }

    IEnumerator FlashRed()
    {
        sr.color = Color.red;
        yield return new WaitForSeconds(0.1f);
        sr.color = Color.white;
    }

    // Угол → кратный 45° → velocity
    void Move8Dir(Vector2 target)
    {
        Vector2 dir     = (target - (Vector2)transform.position).normalized;
        float   angle   = Mathf.Round(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg / 45f) * 45f;
        float   rad     = angle * Mathf.Deg2Rad;
        Vector2 moveDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

        if      (moveDir.x >  0.01f) { sr.flipX = false; facingDir = Vector2.right; }
        else if (moveDir.x < -0.01f) { sr.flipX = true;  facingDir = Vector2.left;  }

        rb.linearVelocity = moveDir * moveSpeed;
    }

    // Три условия: дистанция + сектор 180° + Raycast
    bool CanSeePlayer(bool logBlocked = false)
    {
        if (player == null) return false;
        Vector2 toPlayer = (Vector2)player.position - (Vector2)transform.position;
        float   dist     = toPlayer.magnitude;

        if (dist > detectRange) return false;
        if (Vector2.Angle(facingDir, toPlayer) > 90f) return false;

        bool blocked = Physics2D.Raycast(transform.position, toPlayer.normalized, dist, obstacleLayer).collider != null;

        // Логируем "заблокирован стеной" только при изменении (не каждый кадр)
        if (logBlocked && blocked != wasBlockedLastFrame)
        {
            if (blocked) Debug.Log($"{Tag} игрок в радиусе и секторе, но закрыт стеной");
            wasBlockedLastFrame = blocked;
        }
        if (!blocked) wasBlockedLastFrame = false;

        return !blocked;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.cyan;
        float baseAngle = Mathf.Atan2(facingDir.y, facingDir.x) * Mathf.Rad2Deg;
        DrawSectorLine(baseAngle - 90f);
        DrawSectorLine(baseAngle + 90f);

        if (Application.isPlaying && player != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, player.position);
        }
    }

    void DrawSectorLine(float deg)
    {
        float   rad = deg * Mathf.Deg2Rad;
        Vector3 d   = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * detectRange;
        Gizmos.DrawLine(transform.position, transform.position + d);
    }
}
