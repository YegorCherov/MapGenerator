using System.Collections.Generic;
using UnityEngine;

public static class RectExtensions
{
    public static bool Intersects(this Rect rect, Rect other)
    {
        return rect.max.x >= other.min.x && rect.min.x <= other.max.x &&
               rect.max.y >= other.min.y && rect.min.y <= other.max.y;
    }
}
public class Quadtree
{
    private QuadtreeNode root;
    private int maxObjectsPerNode = 10; // Adjust this value as needed

    public Quadtree(Rect bounds)
    {
        root = new QuadtreeNode(bounds);
    }

    public void Insert(Vector2Int position)
    {
        if (root.Insert(position) && root.Data.Count > maxObjectsPerNode)
            root.Subdivide();
    }

    public List<Vector2Int> QueryRange(Rect range)
    {
        List<Vector2Int> foundObjects = new List<Vector2Int>();
        QueryRangeHelper(root, range, foundObjects);
        return foundObjects;
    }

    private void QueryRangeHelper(QuadtreeNode node, Rect range, List<Vector2Int> foundObjects)
    {
        if (node == null)
            return;

        if (node.Bounds.Intersects(range))
        {
            foreach (Vector2Int position in node.Data)
            {
                if (range.Contains(position))
                    foundObjects.Add(position);
            }

            for (int i = 0; i < 4; i++)
            {
                if (node.Nodes[i] != null)
                    QueryRangeHelper(node.Nodes[i], range, foundObjects);
            }
        }
    }
}