using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System.IO;

// ===================================================================
// Меню: Tools → Build TopDown LabScene
// Собирает top-down сцену: пол, стены, препятствия, игрок, 2 врага.
// ===================================================================
public static class TopDownSceneBuilder
{
    // --- пути ---
    const string TERRAIN_PATH    = "Assets/Pixel Adventure 1/Assets/Terrain/Terrain (16x16) 1.png";
    const string BG_PATH         = "Assets/Pixel Adventure 1/Assets/Background/Blue.png";
    const string MASK_IDLE_PATH  = "Assets/Pixel Adventure 1/Assets/Main Characters/Mask Dude/Idle (32x32).png";
    const string NINJA_IDLE_PATH = "Assets/Pixel Adventure 1/Assets/Main Characters/Ninja Frog/Idle (32x32).png";
    const string PINK_IDLE_PATH  = "Assets/Pixel Adventure 1/Assets/Main Characters/Pink Man/Idle (32x32).png";
    const string TILE_ASSET_DIR  = "Assets/Tiles";
    const string SCENE_PATH      = "Assets/Scenes/LabScene.unity";

    // --- размеры комнаты в тайлах (1 тайл = 1 юнит при PPU=16) ---
    // Комната 20×14. Стены по периметру, внутри пол 18×12.
    // Координаты тайлов: X [-10 .. 9], Y [-7 .. 6]
    const int LEFT   = -10;
    const int RIGHT  =   9;
    const int BOTTOM =  -7;
    const int TOP    =   6;

    // Ортографический размер = высота/2 в юнитах = 14/2 = 7
    const float ORTHO_SIZE = 7f;
    // Центр комнаты по X = (-10+9)/2 = -0.5, по Y = (-7+6)/2 = -0.5
    // Смещаем камеру так чтобы видеть всю комнату с небольшим запасом
    static readonly Vector3 CAM_POS = new Vector3(-0.5f, -0.5f, -10f);

    [MenuItem("Tools/Build TopDown LabScene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureLayer("Obstacle");
        EnsureTag("Player");

        // Гарантируем наличие Tile-ассетов
        EnsureTileAssets();

        // Загружаем тайлы
        var wallTile  = LoadTile("terrain_0_0");    // строка 0 col 0 — тёмный блок (стена)
        var floorTile = LoadTile("terrain_2_0");    // строка 2 col 0 — более светлый (пол)
        if (wallTile == null)
        {
            Debug.LogError("[TopDown] Тайл terrain_0_0 не найден. Сначала запусти Tools → Rebuild LabScene Visual.");
            return;
        }
        // Если тайл пола не найден — используем тот же что для стен (проще)
        if (floorTile == null) floorTile = wallTile;

        // Новая сцена
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // === КАМЕРА ===
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic     = true;
        cam.orthographicSize = ORTHO_SIZE;
        cam.backgroundColor  = new Color(0.1f, 0.1f, 0.2f);
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.nearClipPlane    = -100f;
        cam.farClipPlane     = 100f;
        camGO.transform.position = CAM_POS;

        // === ОСВЕЩЕНИЕ (URP Global Light 2D) ===
        var lightGO  = new GameObject("Global Light 2D");
        var lightType = System.Type.GetType(
            "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
        if (lightType != null)
        {
            var l = lightGO.AddComponent(lightType);
            lightType.GetProperty("intensity")?.SetValue(l, 1.5f);
        }

        // === ФОН ===
        BuildBackground();

        // === GRID + TILEMAPS ===
        var gridGO = new GameObject("Grid");
        gridGO.AddComponent<Grid>();

        // Пол (декоративный, без коллайдера)
        var floorGO = new GameObject("Tilemap_Floor");
        floorGO.transform.SetParent(gridGO.transform);
        var floorTm  = floorGO.AddComponent<Tilemap>();
        var floorRend = floorGO.AddComponent<TilemapRenderer>();
        floorRend.sortingOrder = 0;

        // Стены / препятствия (слой Obstacle, с коллайдерами)
        int obstLayer = LayerMask.NameToLayer("Obstacle");
        var wallGO = new GameObject("Tilemap_Walls");
        wallGO.layer = obstLayer;
        wallGO.transform.SetParent(gridGO.transform);
        var wallTm   = wallGO.AddComponent<Tilemap>();
        var wallRend  = wallGO.AddComponent<TilemapRenderer>();
        wallRend.sortingOrder = 1;
        // Порядок компонентов: сначала Rigidbody2D + CompositeCollider2D, потом TilemapCollider2D
        var wallRB  = wallGO.AddComponent<Rigidbody2D>();
        wallRB.bodyType = RigidbodyType2D.Static;
        var comp    = wallGO.AddComponent<CompositeCollider2D>();
        comp.geometryType = CompositeCollider2D.GeometryType.Polygons;
        var tmCol   = wallGO.AddComponent<TilemapCollider2D>();
        tmCol.compositeOperation = Collider2D.CompositeOperation.Merge;

        // === РИСУЕМ УРОВЕНЬ ===

        // -- Пол (всё внутреннее пространство, включая под стены) --
        for (int x = LEFT; x <= RIGHT; x++)
            for (int y = BOTTOM; y <= TOP; y++)
                SetTile(floorTm, x, y, floorTile);

        // -- Стены по периметру --
        // Нижняя и верхняя
        for (int x = LEFT; x <= RIGHT; x++)
        {
            SetTile(wallTm, x, BOTTOM, wallTile);
            SetTile(wallTm, x, TOP,    wallTile);
        }
        // Левая и правая
        for (int y = BOTTOM + 1; y <= TOP - 1; y++)
        {
            SetTile(wallTm, LEFT,  y, wallTile);
            SetTile(wallTm, RIGHT, y, wallTile);
        }

        // -- Внутренние препятствия (5 штук, все 2×2) --
        // Размещены так чтобы проходы ≥ 2 тайла со всех сторон:
        //   Стена LEFT=-10, первый внутренний X=-9. Блок от X=-7 → проход [-9..-8] = 2 тайла. ✓
        //   Стена RIGHT=9,  первый внутренний X=8.  Блок до X=6  → проход [7..8]   = 2 тайла. ✓
        //   Стена TOP=6,    первый внутренний Y=5.  Блок до Y=3  → проход [4..5]   = 2 тайла. ✓
        //   Стена BOTTOM=-7,первый внутренний Y=-6. Блок от Y=-5 → проход [-6]     = 1 тайл!
        //     → сдвигаем вертикальные блоки: BotL/BotR от Y=-4 → проход [-6..-5] = 2 тайла. ✓
        //
        //  Схема (x: -9 ... 8, y: -6 ... 5):
        //
        //   Y=5:  . . . . . . . . . . . . . . . . . . .
        //   Y=4:  . . . . . . . . . . . . . . . . . . .
        //   Y=3:  . . [BL ] . . . . . . . . . [BR ] . .
        //   Y=2:  . . [BL ] . . . . . . . . . [BR ] . .
        //   Y=1:  . . . . . . . . . . . . . . . . . . .
        //   Y=0:  . . . . . [CT ] . . . . . . . . . . .
        //   Y=-1: . . . . . [CT ] . . . . . . . . . . .
        //   Y=-2: . . . . . . . . . . . . . . . . . . .
        //   Y=-3: . . . . . . . . . . . . . . . . . . .
        //   Y=-4: . . [BotL] . . . . . . . . . [BotR] .
        //   Y=-5: . . [BotL] . . . . . . . . . [BotR] .
        //   Y=-6: . . . . . . . . . . . . . . . . . . .
        //
        // BL:   левый нижний угол (-7,  2)
        // BR:   левый нижний угол ( 5,  2)
        // BotL: левый нижний угол (-7, -5)
        // BotR: левый нижний угол ( 5, -5)
        // CT:   левый нижний угол (-2, -1)
        PaintBlock2x2(wallTm, wallTile, -7,  2);   // BL  — верх-лево
        PaintBlock2x2(wallTm, wallTile,  5,  2);   // BR  — верх-право
        PaintBlock2x2(wallTm, wallTile, -7, -5);   // BotL — низ-лево
        PaintBlock2x2(wallTm, wallTile,  5, -5);   // BotR — низ-право
        PaintBlock2x2(wallTm, wallTile, -2, -1);   // CT  — центр

        // Обновляем коллайдеры
        wallTm.RefreshAllTiles();
        floorTm.RefreshAllTiles();

        // === ИГРОК ===
        // Верхний левый угол — между стеной LEFT и блоком BL(-7,2).
        // Проход X[-9..-8] = 2 тайла. Игрок: (-8.5, 4) — над блоком BL.
        CreatePlayer(new Vector3(-8.5f, 4.5f, 0f));

        // === ВРАГ №1: EnemyBasic (Ninja Frog) ===
        // Старт: правая часть между блоками BR(5,2) и BotR(5,-5).
        // Патруль: верх-право → низ-право → центр (обходит Center CT).
        var p1A = CreatePoint("P1_A", new Vector3(7.5f,  4.5f, 0f));  // верх-право
        var p1B = CreatePoint("P1_B", new Vector3(7.5f, -4.5f, 0f));  // низ-право
        var p1C = CreatePoint("P1_C", new Vector3(2f,   -0.5f, 0f));  // центр-право
        CreateEnemyBasic(new Vector3(7.5f, 0f, 0f), new[] { p1A, p1B, p1C });

        // === ВРАГ №2: EnemyTeleporter (Pink Man) ===
        // Старт: центр-низ, между BotL(-7,-5) и BotR(5,-5).
        // Патруль: центр-лево → центр-право (нижний коридор).
        var p2A = CreatePoint("P2_A", new Vector3(-4.5f, -4.5f, 0f));
        var p2B = CreatePoint("P2_B", new Vector3( 2.5f, -4.5f, 0f));
        CreateEnemyTeleporter(new Vector3(0f, -4.5f, 0f), new[] { p2A, p2B });

        // === СОХРАНЕНИЕ ===
        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        AddSceneToBuildSettings(SCENE_PATH);
        AssetDatabase.Refresh();

        Debug.Log("[TopDown] Сцена создана: " + SCENE_PATH);
        EditorUtility.DisplayDialog("Готово",
            "Top-down сцена собрана!\nPlay → WASD для движения.", "OK");
    }

    // ================================================================
    // Блок 2×2 (левый нижний угол = (bx, by))
    // ================================================================
    static void PaintBlock2x2(Tilemap tm, TileBase tile, int bx, int by)
    {
        SetTile(tm, bx,     by,     tile);
        SetTile(tm, bx + 1, by,     tile);
        SetTile(tm, bx,     by + 1, tile);
        SetTile(tm, bx + 1, by + 1, tile);
    }

    // ================================================================
    // Фон
    // ================================================================
    static void BuildBackground()
    {
        var bg = new GameObject("Background");
        var sr = bg.AddComponent<SpriteRenderer>();
        var sp = AssetDatabase.LoadAssetAtPath<Sprite>(BG_PATH);
        sr.sprite       = sp;
        sr.drawMode     = SpriteDrawMode.Tiled;
        sr.size         = new Vector2(22f, 16f);
        sr.sortingOrder = -10;
        bg.transform.position = new Vector3(-0.5f, -0.5f, 1f);
    }

    // ================================================================
    // Игрок
    // ================================================================
    static void CreatePlayer(Vector3 pos)
    {
        var go  = new GameObject("Player");
        go.tag  = "Player";
        go.transform.position = pos;

        var sr    = go.AddComponent<SpriteRenderer>();
        sr.sprite = LoadFirstSprite(MASK_IDLE_PATH);
        sr.sortingOrder = 5;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.constraints  = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // Круговой коллайдер — не застревает в углах
        var col    = go.AddComponent<CircleCollider2D>();
        col.radius = 0.45f;   // 32px спрайт → 2 юнита; радиус ~0.45 (чуть уже)
        col.offset = Vector2.zero;

        var ph = go.AddComponent<PlayerHealth>();
        ph.hp = 5;

        var pc = go.AddComponent<PlayerController>();
        pc.moveSpeed = 4f;
    }

    // ================================================================
    // EnemyBasic
    // ================================================================
    static void CreateEnemyBasic(Vector3 pos, Transform[] pts)
    {
        var go = new GameObject("EnemyBasic");
        go.transform.position = pos;

        var sr    = go.AddComponent<SpriteRenderer>();
        sr.sprite = LoadFirstSprite(NINJA_IDLE_PATH);
        sr.color  = new Color(0.7f, 1f, 0.7f);
        sr.sortingOrder = 5;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.constraints  = RigidbodyConstraints2D.FreezeRotation;

        var col    = go.AddComponent<CircleCollider2D>();
        col.radius = 0.45f;

        var eb = go.AddComponent<EnemyBasic>();
        // Параметры для top-down (1 юнит = 1 тайл = 16px при PPU=16)
        eb.moveSpeed      = 2.5f;
        eb.detectRange    = 5f;    // 5 тайлов
        eb.attackRange    = 1.2f;  // ~1 тайл
        eb.loseRange      = 7f;    // 7 тайлов
        eb.damage         = 1;
        eb.attackCooldown = 1.5f;
        eb.searchTime     = 3f;
        eb.idleWaitTime   = 2f;
        eb.obstacleLayer  = LayerMask.GetMask("Obstacle");
        eb.patrolPoints   = pts;
    }

    // ================================================================
    // EnemyTeleporter
    // ================================================================
    static void CreateEnemyTeleporter(Vector3 pos, Transform[] pts)
    {
        var go = new GameObject("EnemyTeleporter");
        go.transform.position = pos;

        var sr    = go.AddComponent<SpriteRenderer>();
        sr.sprite = LoadFirstSprite(PINK_IDLE_PATH);
        sr.color  = new Color(1f, 0.7f, 1f);
        sr.sortingOrder = 5;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.constraints  = RigidbodyConstraints2D.FreezeRotation;

        var col    = go.AddComponent<CircleCollider2D>();
        col.radius = 0.45f;

        var et = go.AddComponent<EnemyTeleporter>();
        // scanRadius=1 (ровно 1 юнит по заданию) — сканирует ближний радиус
        et.scanRadius          = 1f;
        et.scanInterval        = 1.5f;
        et.moveSpeed           = 2.5f;
        et.loseRange           = 7f;
        et.damage              = 1;
        et.attackRange         = 1.2f;
        et.attackCooldown      = 1.5f;
        et.searchTime          = 3f;
        et.idleWaitTime        = 2f;
        et.maxTeleportDistance = 7f;
        et.teleportMinDistance = 3f;
        et.teleportCooldown    = 5f;
        et.teleportArrivalDist = 1.5f;
        et.obstacleLayer       = LayerMask.GetMask("Obstacle");
        et.patrolPoints        = pts;
    }

    // ================================================================
    // Helpers
    // ================================================================
    static void SetTile(Tilemap tm, int x, int y, TileBase tile)
        => tm.SetTile(new Vector3Int(x, y, 0), tile);

    static Transform CreatePoint(string name, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.position = pos;
        return go.transform;
    }

    static Sprite LoadFirstSprite(string path)
    {
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
            if (a is Sprite s) return s;
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Tile LoadTile(string tileName)
        => AssetDatabase.LoadAssetAtPath<Tile>($"{TILE_ASSET_DIR}/{tileName}.asset");

    // Гарантируем наличие tile-ассетов (нарезаем terrain если нужно)
    static void EnsureTileAssets()
    {
        if (!Directory.Exists(TILE_ASSET_DIR))
            Directory.CreateDirectory(TILE_ASSET_DIR);

        // Проверяем что terrain нарезан
        var importer = AssetImporter.GetAtPath(TERRAIN_PATH) as TextureImporter;
        if (importer == null) return;

        bool needsSlice = importer.spriteImportMode != SpriteImportMode.Multiple
                       || importer.spritePixelsPerUnit != 16;
        if (needsSlice)
        {
            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Multiple;
            importer.filterMode          = FilterMode.Point;
            importer.textureCompression  = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 16;
            importer.mipmapEnabled       = false;
            importer.alphaIsTransparency = true;

            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            int cols = w / 16;
            int rows = h / 16;
            var sheet = new List<SpriteMetaData>();
            for (int row = rows - 1; row >= 0; row--)
                for (int col = 0; col < cols; col++)
                    sheet.Add(new SpriteMetaData
                    {
                        name      = $"terrain_{(rows - 1 - row)}_{col}",
                        rect      = new Rect(col * 16, row * 16, 16, 16),
                        pivot     = new Vector2(0.5f, 0.5f),
                        alignment = (int)SpriteAlignment.Center
                    });
            importer.spritesheet = sheet.ToArray();
            importer.SaveAndReimport();
        }

        // Создаём tile-ассеты для всех суб-спрайтов
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(TERRAIN_PATH))
        {
            if (!(a is Sprite sp)) continue;
            var tilePath = $"{TILE_ASSET_DIR}/{sp.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            if (existing != null)
            {
                if (existing.sprite != sp) { existing.sprite = sp; EditorUtility.SetDirty(existing); }
            }
            else
            {
                var t = ScriptableObject.CreateInstance<Tile>();
                t.sprite = sp;
                AssetDatabase.CreateAsset(t, tilePath);
            }
        }
        AssetDatabase.SaveAssets();
    }

    static void EnsureLayer(string layerName)
    {
        var tm = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 0; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return;
        for (int i = 8; i < 32; i++)
        {
            var el = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(el.stringValue))
            {
                el.stringValue = layerName;
                tm.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
        }
    }

    static void EnsureTag(string tagName)
    {
        var tm   = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tags = tm.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++)
            if (tags.GetArrayElementAtIndex(i).stringValue == tagName) return;
        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tagName;
        tm.ApplyModifiedPropertiesWithoutUndo();
    }

    static void AddSceneToBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes;
        foreach (var s in scenes) if (s.path == path) return;
        var list = new List<EditorBuildSettingsScene>(scenes)
            { new EditorBuildSettingsScene(path, true) };
        EditorBuildSettings.scenes = list.ToArray();
    }
}
