using System.Collections.Generic;
using UnityEngine;

public class QuadtreeNode
{
    public Rect Bounds;
    public QuadtreeNode[] Nodes = new QuadtreeNode[4];
    public List<Vector2Int> Data = new List<Vector2Int>();

    public QuadtreeNode(Rect bounds)
    {
        Bounds = bounds;
    }

    public bool Insert(Vector2Int position)
    {
        if (!Bounds.Contains(position))
            return false;

        if (Nodes[0] != null)
        {
            int index = GetIndexForPosition(position);
            return Nodes[index].Insert(position);
        }

        Data.Add(position);
        return true;
    }

    private int GetIndexForPosition(Vector2Int position)
    {
        int x = position.x >= Bounds.x + Bounds.width / 2 ? 1 : 0;
        int y = position.y >= Bounds.y + Bounds.height / 2 ? 2 : 0;
        return x + y;
    }

    public void Subdivide()
    {
        float childWidth = Bounds.width / 2;
        float childHeight = Bounds.height / 2;

        Nodes[0] = new QuadtreeNode(new Rect(Bounds.x, Bounds.y, childWidth, childHeight));
        Nodes[1] = new QuadtreeNode(new Rect(Bounds.x + childWidth, Bounds.y, childWidth, childHeight));
        Nodes[2] = new QuadtreeNode(new Rect(Bounds.x, Bounds.y + childHeight, childWidth, childHeight));
        Nodes[3] = new QuadtreeNode(new Rect(Bounds.x + childWidth, Bounds.y + childHeight, childWidth, childHeight));

        for (int i = 0; i < Data.Count; i++)
        {
            Vector2Int position = Data[i];
            int index = GetIndexForPosition(position);
            Nodes[index].Insert(position);
        }

        Data.Clear();
    }
}