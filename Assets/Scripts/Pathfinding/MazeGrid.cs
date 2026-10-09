using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 网格地图：walkable[x,y] 表示该格可通行，cost[x,y] 表示通行代价（供加权算法使用）。
/// </summary>
public class Grid
{
    public readonly int width;
    public readonly int height;
    public readonly bool[,] walkable;
    public readonly float[,] cost;

    public Grid(int width, int height)
    {
        this.width = width;
        this.height = height;
        walkable = new bool[width, height];
        cost = new float[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                cost[x, y] = 1f;
    }

    public bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;
    public bool IsWalkable(int x, int y) => InBounds(x, y) && walkable[x, y];
    public bool IsWalkable(Vector2Int c) => IsWalkable(c.x, c.y);
}

/// <summary>
/// 迷宫生成器：先用递归回溯法（深度优先）生成完美迷宫（任意两点间唯一通路），
/// 再随机打通部分墙制造环路（多解），最后给每格随机通行代价（供 Dijkstra / A* 使用）。
/// </summary>
public static class MazeGenerator
{
    public static Grid Generate(int width, int height, int seed)
    {
        var grid = new Grid(width, height);
        var rng = new System.Random(seed);

        // ---- 递归回溯法（迭代实现，避免深递归）----
        // 通路格位于奇数坐标 (2cx+1, 2cy+1)，墙位于偶数坐标。
        int cw = (width - 1) / 2;
        int ch = (height - 1) / 2;
        bool[,] visited = new bool[cw, ch];
        var stack = new Stack<Vector2Int>();
        Vector2Int[] dirs =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
        };

        stack.Push(new Vector2Int(0, 0));
        visited[0, 0] = true;
        grid.walkable[1, 1] = true;

        while (stack.Count > 0)
        {
            var cur = stack.Peek();
            var options = new List<Vector2Int>(4);
            foreach (var d in dirs)
            {
                var n = cur + d;
                if (n.x >= 0 && n.x < cw && n.y >= 0 && n.y < ch && !visited[n.x, n.y])
                    options.Add(n);
            }

            if (options.Count == 0) { stack.Pop(); continue; }

            var next = options[rng.Next(options.Count)];
            visited[next.x, next.y] = true;

            // 打通 next 格，以及 cur 与 next 之间的墙。
            grid.walkable[2 * next.x + 1, 2 * next.y + 1] = true;
            int wx = 2 * cur.x + 1 + (next.x - cur.x);
            int wy = 2 * cur.y + 1 + (next.y - cur.y);
            grid.walkable[wx, wy] = true;

            stack.Push(next);
        }

        // ---- 随机打通部分墙，制造环路（让多条路径并存）----
        int removals = 0;
        int target = System.Math.Max(1, cw * ch / 6);
        int guard = 0;
        while (removals < target && guard++ < 2000)
        {
            int x = 1 + rng.Next(width - 2);
            int y = 1 + rng.Next(height - 2);
            bool horizontalWall = (x % 2 == 0) && (y % 2 == 1); // 左右两侧是通路
            bool verticalWall = (x % 2 == 1) && (y % 2 == 0);   // 上下两侧是通路
            if ((horizontalWall || verticalWall) && !grid.walkable[x, y])
            {
                grid.walkable[x, y] = true;
                removals++;
            }
        }

        // ---- 随机通行代价（1~4），仅加权算法使用 ----
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (grid.walkable[x, y])
                    grid.cost[x, y] = 1f + rng.Next(4);

        return grid;
    }
}
