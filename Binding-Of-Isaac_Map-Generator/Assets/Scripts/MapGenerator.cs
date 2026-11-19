using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class MapGenerator : MonoBehaviour
{
    [Header("Map Size")]
    [Tooltip("Largura da grade em casas. A primeira e a última coluna ficam sempre vazias.")]
    [SerializeField] private int gridWidth = 15;
    [Tooltip("Altura da grade em casas. A primeira e a última linha ficam sempre vazias.")]
    [SerializeField] private int gridHeight = 15;
    [SerializeField] private int minRooms = 20;
    [SerializeField] private int maxRooms = 35;

    [Header("Generation")]
    [Tooltip("Chance de aceitar uma sala que encosta em 2 salas já existentes. É isso que cria ciclos no grafo.")]
    [SerializeField, Range(0f, 1f)] private float loopChance = 0.45f;
    [Tooltip("Chance de tentar criar uma sala grande (1x2, 2x1, 2x2 ou L).")]
    [SerializeField, Range(0f, 1f)] private float bigRoomChance = 0.3f;
    [Tooltip("Mínimo de salas diferentes ligadas à sala do boss. 1 = o boss é um beco sem saída (como no jogo original); " +
             "2 ou mais = o boss tem várias entradas, então o caminho até ele fica menos óbvio.")]
    [SerializeField, Range(1, 4)] private int minBossNeighbours = 2;
    [Tooltip("0 = mapa aleatório a cada geração. Qualquer outro valor gera sempre o mesmo mapa.")]
    [SerializeField] private int seed = 0;
    [SerializeField] private bool fitCameraToMap = true;

    [Header("Doors")]
    [Tooltip("Tipos de porta sorteados para cada ligação. Cost = peso da aresta; Weight = chance relativa de sair.\n" +
             "Portas baratas são mais comuns e as caras mais raras: assim o caminho com menos salas nem sempre é o mais barato.")]
    [SerializeField] private List<DoorTypeDefinition> doorTypes = new()
    {
        new("Madeira", 1, 6f),
        new("Pedra", 2, 5f),
        new("Ferro", 3, 4f),
        new("Espinhos", 4, 3f),
        new("Trancada", 5, 2.5f),
        new("Fogo", 6, 2f),
        new("Veneno", 7, 1.5f),
        new("Gelo", 8, 1.2f),
        new("Amaldiçoada", 9, 1f),
        new("Lacrada", 10, 0.8f),
    };
    [Tooltip("Ligação com a sala secreta (no jogo, abre com bomba).")]
    [SerializeField] private DoorTypeDefinition secretWall = new("Parede (bomba)", 6, 0f);

    [Header("JSON Export")]
    [Tooltip("Salva o mapa em <projeto>/<pasta>/map.json a cada geração. Tecla S salva uma cópia map_<seed>.json.")]
    [SerializeField] private bool exportJson = true;
    [SerializeField] private string exportFolder = "MapExports";

    [Header("Sprite References")]
    [SerializeField] private Sprite item;
    [SerializeField] private Sprite shop;
    [SerializeField] private Sprite boss;
    [SerializeField] private Sprite secret;

    [Header("Room Variations")]
    [SerializeField] private Sprite largeRoom;
    [SerializeField] private Sprite verticalRoom;
    [SerializeField] private Sprite horizontalRoom;
    [SerializeField] private Sprite lShapeRoom;

    public Cell cellPrefab;

    public static MapGenerator instance;

    private int[] floorPlan;
    public int[] getFloorPlan => floorPlan;
    public int GridWidth => gridWidth;
    public int GridHeight => gridHeight;

    private int floorPlanCount;
    private List<int> endRooms;
    private List<int> bigRoomIndexes;
    private Queue<int> cellQueue;

    private int startIndex;
    public int StartIndex => startIndex;
    private int bossRoomIndex;
    private int secretRoomIndex;
    private int shopRoomIndex;
    private int itemRoomIndex;

    private float cellSize;
    private List<Cell> spawnedCells;
    public List<Cell> getSpawnedCells => spawnedCells;

    // índice da grade -> sala que ocupa aquela casa (inclui a secreta)
    private readonly Dictionary<int, Cell> cellByGridIndex = new();

    // arestas do grafo (uma por par de salas vizinhas)
    private readonly List<MapConnection> connections = new();
    public IReadOnlyList<MapConnection> Connections => connections;

    private int currentSeed;
    private const int MaxGenerationAttempts = 1000;

    // Formatos de sala grande, como deslocamentos (dx, dy) a partir da casa de origem.
    private static readonly Vector2Int[][] roomShapes =
    {
        new[] { new Vector2Int(-1, 0) },
        new[] { new Vector2Int(1, 0) },

        new[] { new Vector2Int(0, 1) },
        new[] { new Vector2Int(0, -1) },

        new[] { new Vector2Int(1, 0), new Vector2Int(0, 1) },
        new[] { new Vector2Int(1, 0), new Vector2Int(1, 1) },
        new[] { new Vector2Int(0, 1), new Vector2Int(1, 1) },

        new[] { new Vector2Int(-1, 1), new Vector2Int(0, 1) },
        new[] { new Vector2Int(-1, 0), new Vector2Int(-1, 1) },
        new[] { new Vector2Int(-1, 0), new Vector2Int(0, 1) },

        new[] { new Vector2Int(1, 0), new Vector2Int(0, -1) },
        new[] { new Vector2Int(1, 0), new Vector2Int(1, -1) },
        new[] { new Vector2Int(1, -1), new Vector2Int(0, -1) },

        new[] { new Vector2Int(-1, 0), new Vector2Int(0, -1) },
        new[] { new Vector2Int(-1, 0), new Vector2Int(-1, -1) },
        new[] { new Vector2Int(0, -1), new Vector2Int(-1, -1) },

        new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) },
        new[] { new Vector2Int(1, 0), new Vector2Int(1, -1), new Vector2Int(0, -1) },
        new[] { new Vector2Int(-1, 0), new Vector2Int(-1, 1), new Vector2Int(0, 1) },
        new[] { new Vector2Int(-1, 0), new Vector2Int(0, -1), new Vector2Int(-1, -1) },
    };

    void Start()
    {
        instance = this;

        cellSize = 0.5f;
        spawnedCells = new();

        SetupDungeon();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            SetupDungeon();
        }

        if (Input.GetKeyDown(KeyCode.S))
        {
            ExportJson(saveCopy: true);
        }
    }

    void SetupDungeon()
    {
        gridWidth = Mathf.Max(gridWidth, 5);
        gridHeight = Mathf.Max(gridHeight, 5);

        currentSeed = seed != 0 ? seed : Environment.TickCount;
        Random.InitState(currentSeed);

        for (int attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            if (!TryGenerateDungeon())
                continue;

            UpdateSpecialRoomVisuals();
            BuildRoomLookup();
            BuildConnections();

            RoomManager.instance.SetupRooms(spawnedCells);

            if (fitCameraToMap) FitCameraToMap();
            if (exportJson) ExportJson(saveCopy: false);
            return;
        }

        Debug.LogError($"Não foi possível gerar um mapa com {minRooms}-{maxRooms} salas numa grade {gridWidth}x{gridHeight}. Aumente a grade ou diminua minRooms.");
    }

    // ---------- Geração da planta ----------

    private bool TryGenerateDungeon()
    {
        ClearCells();

        floorPlan = new int[gridWidth * gridHeight];
        floorPlanCount = 0;
        cellQueue = new Queue<int>();
        endRooms = new List<int>();
        bigRoomIndexes = new List<int>();
        cellByGridIndex.Clear();
        connections.Clear();

        startIndex = ToIndex(gridWidth / 2, gridHeight / 2);
        VisitCell(startIndex, force: true);

        while (cellQueue.Count > 0)
        {
            int index = cellQueue.Dequeue();
            int x = index % gridWidth;
            int y = index / gridWidth;

            bool created = false;

            if (IsInside(x - 1, y)) created |= VisitCell(ToIndex(x - 1, y));
            if (IsInside(x + 1, y)) created |= VisitCell(ToIndex(x + 1, y));
            if (IsInside(x, y - 1)) created |= VisitCell(ToIndex(x, y - 1));
            if (IsInside(x, y + 1)) created |= VisitCell(ToIndex(x, y + 1));

            if (created == false)
                endRooms.Add(index);
        }

        if (floorPlanCount < minRooms)
            return false;

        CleanEndRoomsList();

        return SetupSpecialRooms();
    }

    private void ClearCells()
    {
        foreach (var cell in spawnedCells)
        {
            if (cell != null) Destroy(cell.gameObject);
        }

        spawnedCells.Clear();
    }

    void CleanEndRoomsList()
    {
        endRooms.RemoveAll(index => index == startIndex || bigRoomIndexes.Contains(index) || GetNeighbourCount(index) > 1);
    }

    bool SetupSpecialRooms()
    {
        if (minBossNeighbours <= 1)
        {
            // Comportamento original: o boss é o último beco sem saída gerado.
            bossRoomIndex = endRooms.Count > 0 ? endRooms[endRooms.Count - 1] : -1;

            if (bossRoomIndex != -1)
            {
                endRooms.RemoveAt(endRooms.Count - 1);
            }
        }
        else
        {
            bossRoomIndex = PickBossRoom();

            // Item e loja não podem ficar "atrás" do boss (encostadas só nele).
            if (bossRoomIndex != -1)
                endRooms.RemoveAll(index => AreGridNeighbours(index, bossRoomIndex));
        }

        itemRoomIndex = RandomEndRoom();
        shopRoomIndex = RandomEndRoom();
        secretRoomIndex = PickSecretRoom();

        if (itemRoomIndex == -1 || shopRoomIndex == -1 || bossRoomIndex == -1 || secretRoomIndex == -1)
            return false;

        // A sala secreta não é marcada no floorPlan (fica "escondida" das portas).
        SpawnRoom(secretRoomIndex);

        return true;
    }

    // Escolhe para o boss a sala 1x1 mais distante da inicial que encosta em pelo menos
    // minBossNeighbours salas diferentes. Retorna -1 se nenhuma sala servir (o mapa é gerado de novo).
    int PickBossRoom()
    {
        // casa da grade -> sala que ocupa essa casa (uma sala grande ocupa várias casas)
        var owner = new Dictionary<int, Cell>();
        foreach (var cell in spawnedCells)
        {
            foreach (int index in cell.cellList)
                owner[index] = cell;
        }

        Dictionary<int, int> distance = GridDistancesFromStart();

        int best = -1;
        int bestDistance = -1;

        foreach (var cell in spawnedCells)
        {
            if (cell.roomShape != RoomShape.OneByOne)
                continue; // só existe sprite de boss 1x1

            int index = cell.index;

            if (index == startIndex || !distance.ContainsKey(index))
                continue;

            if (CountNeighbourRooms(index, owner) < minBossNeighbours)
                continue;

            if (distance[index] > bestDistance)
            {
                bestDistance = distance[index];
                best = index;
            }
        }

        return best;
    }

    // Quantas salas DIFERENTES encostam na casa (duas casas da mesma sala grande contam como uma).
    int CountNeighbourRooms(int index, Dictionary<int, Cell> owner)
    {
        int x = index % gridWidth;
        int y = index / gridWidth;
        int[] neighbours = { ToIndex(x - 1, y), ToIndex(x + 1, y), ToIndex(x, y - 1), ToIndex(x, y + 1) };

        var rooms = new HashSet<Cell>();

        foreach (int neighbour in neighbours)
        {
            if (neighbour < 0 || neighbour >= floorPlan.Length || floorPlan[neighbour] != 1)
                continue;

            if (owner.TryGetValue(neighbour, out Cell room))
                rooms.Add(room);
        }

        return rooms.Count;
    }

    // Distância (em casas) de cada casa ocupada até a casa inicial, andando pela grade.
    Dictionary<int, int> GridDistancesFromStart()
    {
        var distance = new Dictionary<int, int> { [startIndex] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(startIndex);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int x = current % gridWidth;
            int y = current / gridWidth;
            int[] neighbours = { ToIndex(x - 1, y), ToIndex(x + 1, y), ToIndex(x, y - 1), ToIndex(x, y + 1) };

            foreach (int next in neighbours)
            {
                if (next < 0 || next >= floorPlan.Length || floorPlan[next] != 1 || distance.ContainsKey(next))
                    continue;

                distance[next] = distance[current] + 1;
                queue.Enqueue(next);
            }
        }

        return distance;
    }

    void UpdateSpecialRoomVisuals()
    {
        foreach (var cell in spawnedCells)
        {
            if (cell.index == itemRoomIndex)
            {
                cell.SetSpecialRoomSprite(item);
                cell.SetRoomType(RoomType.Item);
            }

            if (cell.index == shopRoomIndex)
            {
                cell.SetSpecialRoomSprite(shop);
                cell.SetRoomType(RoomType.Shop);
            }

            if (cell.index == bossRoomIndex)
            {
                cell.SetSpecialRoomSprite(boss);
                cell.SetRoomType(RoomType.Boss);
            }

            if (cell.index == secretRoomIndex)
            {
                cell.SetSpecialRoomSprite(secret);
                cell.SetRoomType(RoomType.Secret);
            }
        }
    }

    int RandomEndRoom()
    {
        if (endRooms.Count == 0) return -1;

        int randomRoom = Random.Range(0, endRooms.Count);
        int index = endRooms[randomRoom];

        endRooms.RemoveAt(randomRoom);

        return index;
    }

    int PickSecretRoom()
    {
        for (int attempt = 0; attempt < 900; attempt++)
        {
            int x = Random.Range(1, gridWidth - 1);
            int y = Random.Range(1, gridHeight - 1);
            int index = ToIndex(x, y);

            if (floorPlan[index] != 0)
                continue;

            if (AreGridNeighbours(index, bossRoomIndex))
                continue;

            int neighbours = GetNeighbourCount(index);

            if (neighbours >= 3 || (attempt > 300 && neighbours >= 2) || (attempt > 600 && neighbours >= 1))
                return index;
        }

        return -1;
    }

    private bool VisitCell(int index, bool force = false)
    {
        if (floorPlan[index] != 0 || floorPlanCount >= maxRooms)
            return false;

        if (!force)
        {
            int neighbours = GetNeighbourCount(index);

            // Com 1 vizinha a sala vira uma "folha" (árvore). Com 2 ela liga duas salas e cria um ciclo.
            if (neighbours > 2) return false;
            if (neighbours == 2 && Random.value >= loopChance) return false;

            if (Random.value < 0.5f) return false;

            if (Random.value < bigRoomChance)
            {
                foreach (var shape in roomShapes.OrderBy(_ => Random.value))
                {
                    if (TryPlaceRoom(index, shape))
                        return true;
                }
            }
        }

        cellQueue.Enqueue(index);
        floorPlan[index] = 1;
        floorPlanCount++;

        SpawnRoom(index);

        return true;
    }

    private void SpawnRoom(int index)
    {
        int x = index % gridWidth;
        int y = index / gridWidth;
        Vector2 position = new Vector2(x * cellSize, -y * cellSize);

        Cell newCell = Instantiate(cellPrefab, position, Quaternion.identity);
        newCell.value = 1;
        newCell.index = index;
        newCell.SetRoomShape(RoomShape.OneByOne);
        newCell.SetRoomType(RoomType.Regular);
        newCell.cellList = new List<int> { index };

        spawnedCells.Add(newCell);
    }

    private bool TryPlaceRoom(int origin, Vector2Int[] offsets)
    {
        int originX = origin % gridWidth;
        int originY = origin / gridWidth;

        List<int> currentRoomIndexes = new List<int>() { origin };

        foreach (var offset in offsets)
        {
            int x = originX + offset.x;
            int y = originY + offset.y;

            if (!IsInside(x, y))
                return false;

            int index = ToIndex(x, y);

            if (floorPlan[index] != 0)
                return false;

            currentRoomIndexes.Add(index);
        }

        foreach (int index in currentRoomIndexes)
        {
            floorPlan[index] = 1;
            floorPlanCount++;
            cellQueue.Enqueue(index);

            bigRoomIndexes.Add(index);
        }

        SpawnLargeRoom(currentRoomIndexes);

        return true;
    }

    private void SpawnLargeRoom(List<int> largeRoomIndexes)
    {
        Cell newCell = null;

        int combinedX = default;
        int combinedY = default;
        float offset = cellSize / 2f;

        for (int i = 0; i < largeRoomIndexes.Count; i++)
        {
            combinedX += largeRoomIndexes[i] % gridWidth;
            combinedY += largeRoomIndexes[i] / gridWidth;
        }

        if (largeRoomIndexes.Count == 4)
        {
            Vector2 position = new Vector2(combinedX / 4 * cellSize + offset, -combinedY / 4 * cellSize - offset);

            newCell = Instantiate(cellPrefab, position, Quaternion.identity);
            newCell.SetRoomSprite(largeRoom);
            newCell.SetRoomShape(RoomShape.TwoByTwo);
        }

        if (largeRoomIndexes.Count == 3)
        {
            Vector2 position = new Vector2(combinedX / 3 * cellSize + offset, -combinedY / 3 * cellSize - offset);
            newCell = Instantiate(cellPrefab, position, Quaternion.identity);
            newCell.SetRoomSprite(lShapeRoom);
            newCell.RotateCell(largeRoomIndexes, gridWidth);
            newCell.SetRoomShape(RoomShape.LShape);
        }

        if (largeRoomIndexes.Count == 2)
        {
            if (largeRoomIndexes[0] + gridWidth == largeRoomIndexes[1] || largeRoomIndexes[0] - gridWidth == largeRoomIndexes[1])
            {
                Vector2 position = new Vector2(combinedX / 2 * cellSize, -combinedY / 2 * cellSize - offset);
                newCell = Instantiate(cellPrefab, position, Quaternion.identity);
                newCell.SetRoomSprite(verticalRoom);
                newCell.SetRoomShape(RoomShape.OneByTwo);
            }
            else if (largeRoomIndexes[0] + 1 == largeRoomIndexes[1] || largeRoomIndexes[0] - 1 == largeRoomIndexes[1])
            {
                Vector2 position = new Vector2(combinedX / 2 * cellSize + offset, -combinedY / 2 * cellSize);
                newCell = Instantiate(cellPrefab, position, Quaternion.identity);
                newCell.SetRoomSprite(horizontalRoom);
                newCell.SetRoomShape(RoomShape.TwoByOne);
            }
        }

        newCell.cellList = largeRoomIndexes;
        newCell.cellList.Sort();
        newCell.index = newCell.cellList[0];
        newCell.SetRoomType(RoomType.Regular);
        spawnedCells.Add(newCell);
    }

    // ---------- Grafo: ids das salas e portas ----------

    private void BuildRoomLookup()
    {
        cellByGridIndex.Clear();

        for (int i = 0; i < spawnedCells.Count; i++)
        {
            spawnedCells[i].id = i;

            foreach (int index in spawnedCells[i].cellList)
            {
                cellByGridIndex[index] = spawnedCells[i];
            }
        }
    }

    private void BuildConnections()
    {
        connections.Clear();

        var seen = new HashSet<(int, int)>();
        int[] steps = { -1, 1, -gridWidth, gridWidth };

        foreach (var cell in spawnedCells)
        {
            foreach (int index in cell.cellList)
            {
                foreach (int step in steps)
                {
                    int neighbourIndex = index + step;

                    if (neighbourIndex < 0 || neighbourIndex >= floorPlan.Length)
                        continue;

                    if (!cellByGridIndex.TryGetValue(neighbourIndex, out Cell other) || other == cell)
                        continue;

                    int a = Mathf.Min(cell.id, other.id);
                    int b = Mathf.Max(cell.id, other.id);

                    if (!seen.Add((a, b)))
                        continue;

                    bool touchesSecret = cell.roomType == RoomType.Secret || other.roomType == RoomType.Secret;
                    DoorTypeDefinition door = touchesSecret ? secretWall : PickDoorType();

                    connections.Add(new MapConnection { from = a, to = b, doorType = door.name, cost = door.cost });
                }
            }
        }

        OrientConnectionsFromStart();
    }

    // Orienta cada porta no sentido de progressão do mapa: "from" é a sala mais perto da
    // inicial (em número de portas) e "to" a mais longe. Em empate, o id menor vem primeiro.
    // Seguindo from -> to nunca se volta para uma sala anterior, então as portas orientadas
    // formam um grafo direcionado sem ciclos (DAG), que é o que a ordenação topológica precisa.
    private void OrientConnectionsFromStart()
    {
        int startId = cellByGridIndex[startIndex].id;

        var neighbours = new Dictionary<int, List<int>>();
        foreach (var cell in spawnedCells)
            neighbours[cell.id] = new List<int>();

        foreach (var connection in connections)
        {
            neighbours[connection.from].Add(connection.to);
            neighbours[connection.to].Add(connection.from);
        }

        var depth = new Dictionary<int, int> { [startId] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(startId);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();

            foreach (int next in neighbours[current])
            {
                if (depth.ContainsKey(next))
                    continue;

                depth[next] = depth[current] + 1;
                queue.Enqueue(next);
            }
        }

        foreach (var connection in connections)
        {
            int depthFrom = depth.TryGetValue(connection.from, out int df) ? df : int.MaxValue;
            int depthTo = depth.TryGetValue(connection.to, out int dt) ? dt : int.MaxValue;

            bool swap = depthTo < depthFrom || (depthTo == depthFrom && connection.to < connection.from);

            if (swap)
                (connection.from, connection.to) = (connection.to, connection.from);
        }
    }

    private DoorTypeDefinition PickDoorType()
    {
        float total = doorTypes.Sum(d => Mathf.Max(0f, d.weight));

        if (total <= 0f)
            return doorTypes.Count > 0 ? doorTypes[0] : new DoorTypeDefinition("Normal", 1, 1f);

        float roll = Random.value * total;

        foreach (var door in doorTypes)
        {
            roll -= Mathf.Max(0f, door.weight);
            if (roll <= 0f) return door;
        }

        return doorTypes[doorTypes.Count - 1];
    }

    // ---------- Exportação ----------

    private void ExportJson(bool saveCopy)
    {
        if (spawnedCells == null || spawnedCells.Count == 0 || cellByGridIndex.Count == 0)
            return;

        var export = new MapExport
        {
            seed = currentSeed,
            gridWidth = gridWidth,
            gridHeight = gridHeight,
            startRoom = cellByGridIndex[startIndex].id,
            bossRoom = cellByGridIndex[bossRoomIndex].id,
            rooms = spawnedCells.Select(ToRoomExport).ToArray(),
            doors = connections.Select(c => new DoorExport { from = c.from, to = c.to, type = c.doorType, cost = c.cost }).ToArray(),
        };

        string json = JsonUtility.ToJson(export, true);

        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", exportFolder));
        Directory.CreateDirectory(folder);

        string fileName = saveCopy ? $"map_{currentSeed}.json" : "map.json";
        string path = Path.Combine(folder, fileName);
        File.WriteAllText(path, json);

        Debug.Log($"Mapa exportado ({spawnedCells.Count} salas, {connections.Count} portas): {path}");
    }

    private RoomExport ToRoomExport(Cell cell)
    {
        int minIndex = cell.cellList.Min();

        return new RoomExport
        {
            id = cell.id,
            type = cell.roomType.ToString(),
            shape = cell.roomShape.ToString(),
            gridX = minIndex % gridWidth,
            gridY = minIndex / gridWidth,
            cells = cell.cellList.ToArray(),
        };
    }

    // ---------- Câmera ----------

    private void FitCameraToMap()
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic || cellByGridIndex.Count == 0)
            return;

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;

        foreach (int index in cellByGridIndex.Keys)
        {
            int x = index % gridWidth;
            int y = index / gridWidth;
            minX = Mathf.Min(minX, x);
            maxX = Mathf.Max(maxX, x);
            minY = Mathf.Min(minY, y);
            maxY = Mathf.Max(maxY, y);
        }

        float centerX = (minX + maxX) / 2f * cellSize;
        float centerY = -(minY + maxY) / 2f * cellSize;
        cam.transform.position = new Vector3(centerX, centerY, cam.transform.position.z);

        float halfHeight = (maxY - minY + 1) * cellSize / 2f + cellSize;
        float halfWidth = (maxX - minX + 1) * cellSize / 2f + cellSize;
        cam.orthographicSize = Mathf.Max(halfHeight, halfWidth / cam.aspect);
    }

    // ---------- Utilidades da grade ----------

    private int ToIndex(int x, int y) => y * gridWidth + x;

    // Casas onde salas podem existir (deixa uma borda vazia para não "dar a volta" na grade).
    private bool IsInside(int x, int y) => x >= 1 && x <= gridWidth - 2 && y >= 1 && y <= gridHeight - 2;

    private int OccupiedAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= gridWidth || y >= gridHeight) return 0;
        return floorPlan[ToIndex(x, y)];
    }

    private int GetNeighbourCount(int index)
    {
        int x = index % gridWidth;
        int y = index / gridWidth;
        return OccupiedAt(x - 1, y) + OccupiedAt(x + 1, y) + OccupiedAt(x, y - 1) + OccupiedAt(x, y + 1);
    }

    private bool AreGridNeighbours(int a, int b)
    {
        if (a < 0 || b < 0) return false;
        int dx = Mathf.Abs(a % gridWidth - b % gridWidth);
        int dy = Mathf.Abs(a / gridWidth - b / gridWidth);
        return dx + dy == 1;
    }
}
