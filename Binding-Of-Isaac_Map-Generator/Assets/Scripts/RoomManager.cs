using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RoomManager : MonoBehaviour
{
    private List<Room> createdRooms;

    [Header("Offset Variables")]
    public float offsetX;
    public float offsetY;

    [Header("Prefab References")]
    public Room roomPrefab;
    public Door doorPrefab;

    [Header("Scriptable Object References")]
    public DoorScriptable[] doors;
    public RoomScriptable[] rooms;

    [Header("Start Room Marker")]
    [Tooltip("Ícone da sala inicial. Se ficar vazio, uma estrela é desenhada automaticamente.")]
    public Sprite startRoomIcon;
    public Color startRoomIconColor = new Color(1f, 0.85f, 0.1f);
    [Tooltip("Tamanho da estrela na sala grande.")]
    public float startRoomIconSize = 1.5f;
    [Tooltip("Tamanho da estrela na casa do minimapa (o que aparece na tela).")]
    public float startCellIconSize = 0.35f;

    private Sprite generatedStarSprite;

    public static RoomManager instance;

    private void Awake()
    {
        instance = this;
        createdRooms = new List<Room>();
    }

    public void SetupRooms(List<Cell> spawnedCells)
    {
        for(int i = createdRooms.Count - 1; i >= 0; i--)
        {
            Destroy(createdRooms[i].gameObject);
        }

        createdRooms.Clear();

        foreach(var currentCell in spawnedCells)
        {
            var foundRoom = rooms.FirstOrDefault(x => x.roomShape == currentCell.roomShape && x.roomType == currentCell.roomType && DoesTileMatchCell(x.occupiedTiles, currentCell));

            var currentPosition = currentCell.transform.position;

            var convertedPosition = new Vector2(currentPosition.x * offsetX, currentPosition.y * offsetY);

            var spawnedRoom = Instantiate(roomPrefab, convertedPosition, Quaternion.identity);

            spawnedRoom.SetupRoom(currentCell, foundRoom);

            if (currentCell.cellList.Contains(MapGenerator.instance.StartIndex))
            {
                // Na sala grande (acima da sala = 0 e das portas = 3)...
                AddStartMarker(spawnedRoom.transform, spawnedRoom.spriteRenderer.sortingLayerID, 5, startRoomIconSize);
                // ...e na casa do minimapa, que é o que a câmera mostra (acima do ícone da casa = 1).
                AddStartMarker(currentCell.transform, currentCell.spriteRenderer.sortingLayerID, 3, startCellIconSize);
            }

            createdRooms.Add(spawnedRoom);
        }
    }

    // ---------- Marcador da sala inicial ----------

    private void AddStartMarker(Transform parent, int sortingLayerID, int sortingOrder, float worldSize)
    {
        var marker = new GameObject("Start Marker");
        // Filho do objeto: é destruído junto quando o mapa é gerado de novo.
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        marker.transform.rotation = Quaternion.identity; // estrela sempre em pé, mesmo em sala rotacionada

        var renderer = marker.AddComponent<SpriteRenderer>();
        renderer.sortingLayerID = sortingLayerID;
        renderer.sortingOrder = sortingOrder;

        Sprite sprite;
        if (startRoomIcon != null)
        {
            sprite = startRoomIcon;
        }
        else
        {
            if (generatedStarSprite == null)
                generatedStarSprite = CreateStarSprite(64, 1f);

            sprite = generatedStarSprite;
            renderer.color = startRoomIconColor;
        }

        renderer.sprite = sprite;

        // Ajusta a escala para a estrela ter worldSize unidades, qualquer que seja a escala do pai.
        float largest = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
        float parentScale = Mathf.Abs(parent.lossyScale.x) > 0f ? Mathf.Abs(parent.lossyScale.x) : 1f;
        float scale = largest > 0f ? worldSize / largest / parentScale : 1f;
        marker.transform.localScale = new Vector3(scale, scale, 1f);
    }

    // Desenha uma estrela de 5 pontas branca com contorno escuro (a cor vem do SpriteRenderer).
    private static Sprite CreateStarSprite(int resolution, float worldSize)
    {
        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2(resolution / 2f, resolution / 2f);
        float outer = resolution * 0.48f;
        float inner = outer * 0.45f;
        Vector2[] outline = BuildStar(center, outer, inner);
        Vector2[] fill = BuildStar(center, outer * 0.82f, inner * 0.82f);

        var pixels = new Color32[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                Color32 c = new Color32(0, 0, 0, 0);
                if (IsInsidePolygon(p, fill))
                    c = new Color32(255, 255, 255, 255);
                else if (IsInsidePolygon(p, outline))
                    c = new Color32(60, 40, 0, 255);
                pixels[y * resolution + x] = c;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f), resolution / worldSize);
    }

    private static Vector2[] BuildStar(Vector2 center, float outerRadius, float innerRadius)
    {
        var points = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float angle = Mathf.PI / 2f + i * Mathf.PI / 5f; // começa apontando para cima
            float radius = i % 2 == 0 ? outerRadius : innerRadius;
            points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }

    private static bool IsInsidePolygon(Vector2 p, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private bool DoesTileMatchCell(int[] occupiedTiles, Cell cell)
    {
        if(occupiedTiles.Length != cell.cellList.Count)
            return false;

        int gridWidth = MapGenerator.instance.GridWidth;
        int minIndex = cell.cellList.Min();
        List<int> normalizedCell = new List<int>();

        foreach(int index in cell.cellList)
        {
            int dx = (index % gridWidth) - (minIndex % gridWidth);
            int dy = (index / gridWidth) - (minIndex / gridWidth);

            // occupiedTiles nos assets usa sempre largura 10 (ex.: 2x2 = 0, 1, 10, 11)
            normalizedCell.Add(dy * 10 + dx);
        }

        normalizedCell.Sort();
        int[] sortedOccupied = (int[])occupiedTiles.Clone();
        Array.Sort(sortedOccupied);

        return normalizedCell.SequenceEqual(sortedOccupied);
    }
}
