using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
public class EnemyTeleporter : MonoBehaviour
{
    public enum State { Idle, Patrol, Chase, Attack, Search, Teleport }
    public State currentState = State.Idle;

    public float moveSpeed           = 2f;
    public float scanRadius          = 1f;    // радиус скана 360°, ровно 1 метр по заданию
    public float scanInterval        = 1.5f;
    public float loseRange           = 8f;
    public int   damage              = 1;
    public float attackRange         = 1.2f;
    public float attackCooldown      = 1.5f;
    public float searchTime          = 3f;
    public float idleWaitTime        = 2f;
    public float maxTeleportDistance = 6f;
    public float teleportMinDistance = 3f;
    public float teleportCooldown    = 5f;
    public float teleportArrivalDist = 1.5f;
    public LayerMask   obstacleLayer;
    public Transform[] patrolPoints;

    Rigidbody2D    rb;
    SpriteRenderer sr;
    Transform      player;
    int     patrolIndex   = 0;
    float   stateTimer    = 0f;
    float   attackTimer   = 0f;
    float   scanTimer     = 0f;
    float   teleportTimer = 0f;
    Vector2 facingDir     = Vector2.right;
    Vector2 lastKnownPos;
    bool    playerKnown   = false;
    bool    isTeleporting = false;
    bool    arrivedAtSearch = false;

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
        if (attackTimer  > 0f) attackTimer  -= Time.deltaTime;
        if (teleportTimer > 0f) teleportTimer -= Time.deltaTime;

        // Периодический скан 360° — вне атаки и телепорта
        if (currentState != State.Attack && currentState != State.Teleport)
        {
            scanTimer += Time.deltaTime;
            if (scanTimer >= scanInterval) { scanTimer = 0f; DoScan(); }
        }

        // Телепорт: только в Chase, кулдаун прошёл, игрок в нужном диапазоне
        if (currentState == State.Chase && playerKnown && !isTeleporting && teleportTimer <= 0f)
        {
            float d = Vector2.Distance(transform.position, player.position);
            if (d >= teleportMinDistance && d <= maxTeleportDistance)
            { ChangeState(State.Teleport, "игрок далеко, способность готова"); return; }
        }

        switch (currentState)
        {
            case State.Idle:     UpdateIdle();     break;
            case State.Patrol:   UpdatePatrol();   break;
            case State.Chase:    UpdateChase();    break;
            case State.Attack:   UpdateAttack();   break;
            case State.Search:   UpdateSearch();   break;
            case State.Teleport: break; // управляется корутиной
        }
    }

    void ChangeState(State next, string reason)
    {
        Debug.Log($"{Tag} {currentState} -> {next} ({reason})");
        currentState    = next;
        stateTimer      = 0f;
        arrivedAtSearch = false;
        if (next == State.Idle || next == State.Search) rb.linearVelocity = Vector2.zero;
        if (next == State.Teleport)
        {
            rb.linearVelocity = Vector2.zero;
            if (!isTeleporting) StartCoroutine(TeleportRoutine());
        }
    }

    void UpdateIdle()
    {
        rb.linearVelocity = Vector2.zero;
        stateTimer += Time.deltaTime;
        if (playerKnown)                ChangeState(State.Chase, "скан обнаружил игрока");
        else if (stateTimer >= idleWaitTime) ChangeState(State.Patrol, "ожидание истекло");
    }

    void UpdatePatrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) { ChangeState(State.Idle, "нет точек"); return; }
        if (playerKnown) { ChangeState(State.Chase, "скан обнаружил игрока"); return; }

        var t = patrolPoints[patrolIndex];
        MoveTo(t.position);

        if (Vector2.Distance(transform.position, t.position) < 0.15f)
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

        if (dist <= attackRange) { ChangeState(State.Attack, "игрок в радиусе атаки"); return; }
        if (dist > loseRange)
        {
            Debug.Log($"{Tag} потерял игрока, последняя позиция ({lastKnownPos.x:F1}, {lastKnownPos.y:F1})");
            playerKnown = false;
            ChangeState(State.Search, "игрок слишком далеко");
            return;
        }
        MoveTo(player.position);
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
        if (playerKnown) { ChangeState(State.Chase, "скан обнаружил игрока в поиске"); return; }

        if (Vector2.Distance(transform.position, lastKnownPos) > 0.2f)
        {
            MoveTo(lastKnownPos);
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
                playerKnown = false;
                ChangeState(State.Patrol, "поиск истёк");
            }
        }
    }

    IEnumerator TeleportRoutine()
    {
        isTeleporting = true;
        Vector2 from = transform.position;

        Vector2 dirToEnemy = ((Vector2)transform.position - (Vector2)player.position).normalized;
        Vector2 targetPos  = (Vector2)player.position + dirToEnemy * teleportArrivalDist;

        if (Physics2D.OverlapCircle(targetPos, 0.2f, obstacleLayer) != null)
        {
            Debug.Log($"{Tag} телепорт отменён — точка ({targetPos.x:F1}, {targetPos.y:F1}) внутри стены");
            teleportTimer = teleportCooldown;
            isTeleporting = false;
            ChangeState(State.Chase, "телепорт заблокирован");
            yield break;
        }

        sr.enabled = false;
        yield return new WaitForSeconds(0.2f);

        transform.position = new Vector3(targetPos.x, targetPos.y, transform.position.z);
        teleportTimer      = teleportCooldown;
        sr.enabled         = true;

        Debug.Log($"{Tag} телепорт выполнен: ({from.x:F1},{from.y:F1}) → ({targetPos.x:F1},{targetPos.y:F1}), перезарядка {teleportCooldown:F0} с");
        isTeleporting = false;
        ChangeState(State.Chase, "после телепорта");
    }

    void DoScan()
    {
        bool found = false;
        foreach (Collider2D col in Physics2D.OverlapCircleAll(transform.position, scanRadius))
        {
            if (!col.CompareTag("Player")) continue;
            Vector2 to      = (Vector2)col.transform.position - (Vector2)transform.position;
            bool    blocked = Physics2D.Raycast(transform.position, to.normalized, to.magnitude, obstacleLayer).collider != null;
            if (!blocked)
            {
                found        = true;
                playerKnown  = true;
                lastKnownPos = col.transform.position;
                if (currentState != State.Chase && currentState != State.Attack && currentState != State.Teleport)
                    ChangeState(State.Chase, "скан 360° нашёл игрока");
            }
        }
        Debug.Log($"{Tag} скан 360° — {(found ? "игрок найден" : "не найден")}");

        // Логируем когда кулдаун телепорта прошёл (один раз)
        // (просто через DoScan — будет видно в логах когда снова стал доступен)
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

    void MoveTo(Vector2 target)
    {
        Vector2 dir = (target - (Vector2)transform.position).normalized;
        rb.linearVelocity = dir * moveSpeed;
        if      (dir.x >  0.01f) { sr.flipX = false; facingDir = Vector2.right; }
        else if (dir.x < -0.01f) { sr.flipX = true;  facingDir = Vector2.left;  }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, scanRadius);
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, teleportMinDistance);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, maxTeleportDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, loseRange);
        Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
