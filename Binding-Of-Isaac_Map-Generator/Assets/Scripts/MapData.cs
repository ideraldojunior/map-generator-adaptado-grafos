using System;

// Tipos de dados do mapa usados pelo gerador e pela exportação em JSON.

// Um tipo de porta possível (custo = peso da aresta no grafo).
[Serializable]
public class DoorTypeDefinition
{
    public string name;
    public int cost;
    public float weight; // chance relativa de ser sorteada

    public DoorTypeDefinition() { }

    public DoorTypeDefinition(string name, int cost, float weight)
    {
        this.name = name;
        this.cost = cost;
        this.weight = weight;
    }
}

// Uma ligação (aresta) entre duas salas.
// from = sala mais perto da inicial, to = sala mais longe (sentido de progressão).
public class MapConnection
{
    public int from;
    public int to;
    public string doorType;
    public int cost;
}

// ---------- Formato do JSON exportado ----------

[Serializable]
public class RoomExport
{
    public int id;
    public string type;
    public string shape;
    public int gridX;     // coluna da casa de menor índice da sala
    public int gridY;     // linha da casa de menor índice da sala
    public int[] cells;   // índices da grade ocupados (índice = y * gridWidth + x)
}

[Serializable]
public class DoorExport
{
    public int from;
    public int to;
    public string type;
    public int cost;
}

[Serializable]
public class MapExport
{
    public int seed;
    public int gridWidth;
    public int gridHeight;
    public int startRoom;
    public int bossRoom;
    public RoomExport[] rooms;
    public DoorExport[] doors;
}
