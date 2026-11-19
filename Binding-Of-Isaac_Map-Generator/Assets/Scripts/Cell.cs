using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.AI;

public enum RoomType
{
    Regular,
    Item,
    Shop,
    Boss,
    Secret
}

public enum RoomShape
{
    OneByOne,
    OneByTwo,
    TwoByOne,
    TwoByTwo,
    LShape
}

public class Cell : MonoBehaviour
{
    public RoomType roomType;
    public RoomShape roomShape;

    public int id;     // número do nó no grafo (posição em spawnedCells)
    public int index;  // menor índice da grade ocupado pela sala
    public int value;

    public SpriteRenderer spriteRenderer;
    public SpriteRenderer roomSprite;

    public List<int> cellList = new List<int>();

    public void SetSpecialRoomSprite(Sprite icon)
    {
        spriteRenderer.sprite = icon;
    }

    public void SetRoomSprite(Sprite roomIcon)
    {
        roomSprite.sprite = roomIcon;
    }

    public void SetRoomType(RoomType newRoomType)
    {
        roomType = newRoomType;
    }

    public void SetRoomShape(RoomShape newRoomShape)
    {
        roomShape = newRoomShape;
    }

    public void RotateCell(List<int> connectedCells, int gridWidth)
    {
        connectedCells.Sort();
        index = connectedCells[0];

        if(connectedCells.Contains(index + 1) && connectedCells.Contains(index + gridWidth))
        {
            ApplyRotation(-90);
        }
        else if(connectedCells.Contains(index + 1) && connectedCells.Contains(index + gridWidth + 1))
        {
            ApplyRotation(180);
        }
        else if (connectedCells.Contains(index + gridWidth - 1) && connectedCells.Contains(index + gridWidth))
        {
            ApplyRotation(90);
        }
    }

    public void ApplyRotation(float angle)
    {
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }
}
