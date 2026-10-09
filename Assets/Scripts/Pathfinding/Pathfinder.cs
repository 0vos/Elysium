using System.Collections.Generic;
using UnityEngine;

public enum PathAlgorithm
{
    BFS,       // 广度优先：最少步数
    Dijkstra,  // 迪杰斯特拉：加权代价最短
    AStar,     // A*：启发式，更快
    DFS,       // 深度优先（迭代加深 / 深度受限）
    QLearning  // Q-Learning 强化学习
}

public struct PathResult
{
    public bool found;
    public List<Vector2Int> path;    // 含起点与终点
    public List<Vector2Int> visited; // 搜索过程中访问（扩展）的格子
    public float cost;               // 路径总代价 / 长度
    public int steps;                // 扩展次数 / 迭代步数
    public string note;              // 人类可读说明
    public float[,] valueMap;        // Q-Learning 学到的状态价值（其它算法为 null）
}

/// <summary>
/// 路径搜索算法库（纯逻辑，不依赖场景对象，方便单元测试）：
/// BFS、Dijkstra、A*、DFS（迭代加深/深度受限）、Q-Learning。
/// </summary>
public static class Pathfinder
{
    static readonly Vector2Int[] Dirs =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    public static PathResult Solve(Grid grid, Vector2Int start, Vector2Int goal, PathAlgorithm algo)
    {
        switch (algo)
        {
            case PathAlgorithm.BFS:       return BFS(grid, start, goal);
            case PathAlgorithm.Dijkstra:  return Dijkstra(grid, start, goal);
            case PathAlgorithm.AStar:     return AStar(grid, start, goal);
            case PathAlgorithm.DFS:       return IDDFS(grid, start, goal);
            case PathAlgorithm.QLearning: return QLearning(grid, start, goal);
            default:                      return BFS(grid, start, goal);
        }
    }

    // ============ 工具 ============

    static List<Vector2Int> Neighbors(Grid grid, Vector2Int c)
    {
        var list = new List<Vector2Int>(4);
        foreach (var d in Dirs)
        {
            var n = c + d;
            if (grid.IsWalkable(n)) list.Add(n);
        }
        return list;
    }

    static List<Vector2Int> Reconstruct(Dictionary<Vector2Int, Vector2Int> parent,
        Vector2Int start, Vector2Int goal)
    {
        var path = new List<Vector2Int>();
        var cur = goal;
        int guard = 0;
        while (cur != start)
        {
            path.Add(cur);
            if (!parent.TryGetValue(cur, out cur)) break; // 安全兜底
            if (++guard > parent.Count + 2) break;
        }
        path.Add(start);
        path.Reverse();
        return path;
    }

    static int Manhattan(Vector2Int a, Vector2Int b)
        => System.Math.Abs(a.x - b.x) + System.Math.Abs(a.y - b.y);

    // ============ BFS：广度优先（最少步数） ============

    public static PathResult BFS(Grid grid, Vector2Int start, Vector2Int goal)
    {
        var res = new PathResult();
        var parent = new Dictionary<Vector2Int, Vector2Int>();
        var visited = new List<Vector2Int>();
        var seen = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();

        queue.Enqueue(start);
        seen.Add(start);
        int steps = 0;

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            visited.Add(cur);
            steps++;
            if (cur == goal) { res.found = true; break; }

            foreach (var n in Neighbors(grid, cur))
                if (seen.Add(n)) { parent[n] = cur; queue.Enqueue(n); }
        }

        res.visited = visited;
        res.steps = steps;
        if (res.found)
        {
            res.path = Reconstruct(parent, start, goal);
            res.cost = res.path.Count - 1;
            res.note = "BFS 找到路径，长度 " + res.cost + " 步（最少步数），扩展 " + steps + " 个节点";
        }
        else res.note = "BFS 未找到路径";
        return res;
    }

    // ============ Dijkstra：加权最短路径 ============

    public static PathResult Dijkstra(Grid grid, Vector2Int start, Vector2Int goal)
    {
        var res = new PathResult();
        int w = grid.width, h = grid.height;
        var dist = new float[w, h];
        var done = new bool[w, h];
        var parent = new Dictionary<Vector2Int, Vector2Int>();
        var visited = new List<Vector2Int>();

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                dist[x, y] = float.MaxValue;
        dist[start.x, start.y] = 0f;
        int steps = 0;

        while (true)
        {
            // 线性扫描取 dist 最小的未访问节点（网格很小，足够快且实现简单可靠）。
            Vector2Int u = new Vector2Int(-1, -1);
            float best = float.MaxValue;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (!done[x, y] && dist[x, y] < best)
                    { best = dist[x, y]; u = new Vector2Int(x, y); }

            if (u.x < 0) break; // 没有可达节点了
            done[u.x, u.y] = true;
            visited.Add(u);
            steps++;
            if (u == goal) { res.found = true; break; }

            foreach (var n in Neighbors(grid, u))
            {
                float nd = dist[u.x, u.y] + grid.cost[n.x, n.y];
                if (nd < dist[n.x, n.y])
                {
                    dist[n.x, n.y] = nd;
                    parent[n] = u;
                }
            }
        }

        res.visited = visited;
        res.steps = steps;
        if (res.found)
        {
            res.path = Reconstruct(parent, start, goal);
            res.cost = dist[goal.x, goal.y];
            res.note = "Dijkstra 找到路径，代价 " + res.cost.ToString("F1") + "，扩展 " + steps + " 个节点";
        }
        else res.note = "Dijkstra 未找到路径";
        return res;
    }

    // ============ A*：启发式搜索 ============

    public static PathResult AStar(Grid grid, Vector2Int start, Vector2Int goal)
    {
        var res = new PathResult();
        int w = grid.width, h = grid.height;
        var g = new float[w, h];
        var closed = new bool[w, h];
        var parent = new Dictionary<Vector2Int, Vector2Int>();
        var visited = new List<Vector2Int>();
        var open = new List<Vector2Int>();

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                g[x, y] = float.MaxValue;
        g[start.x, start.y] = 0f;
        open.Add(start);
        int steps = 0;

        while (open.Count > 0)
        {
            // 取 f = g + h 最小的节点。
            int bestIdx = 0;
            float bestF = float.MaxValue;
            for (int i = 0; i < open.Count; i++)
            {
                var c = open[i];
                float f = g[c.x, c.y] + Manhattan(c, goal);
                if (f < bestF) { bestF = f; bestIdx = i; }
            }
            var u = open[bestIdx];
            open.RemoveAt(bestIdx);

            if (closed[u.x, u.y]) continue;
            closed[u.x, u.y] = true;
            visited.Add(u);
            steps++;
            if (u == goal) { res.found = true; break; }

            foreach (var n in Neighbors(grid, u))
            {
                if (closed[n.x, n.y]) continue;
                float ng = g[u.x, u.y] + grid.cost[n.x, n.y];
                if (ng < g[n.x, n.y])
                {
                    g[n.x, n.y] = ng;
                    parent[n] = u;
                    open.Add(n);
                }
            }
        }

        res.visited = visited;
        res.steps = steps;
        if (res.found)
        {
            res.path = Reconstruct(parent, start, goal);
            res.cost = g[goal.x, goal.y];
            res.note = "A* 找到路径，代价 " + res.cost.ToString("F1") + "，扩展 " + steps + " 个节点";
        }
        else res.note = "A* 未找到路径";
        return res;
    }

    // ============ DFS：深度优先（迭代加深，内含深度受限搜索） ============

    public static PathResult IDDFS(Grid grid, Vector2Int start, Vector2Int goal)
    {
        var res = new PathResult();
        int maxDepth = grid.width * grid.height;
        var allVisited = new List<Vector2Int>();
        int steps = 0;

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            var visited = new List<Vector2Int>();
            var path = DepthLimitedDFS(grid, start, goal, depth, visited, ref steps);
            allVisited.AddRange(visited);
            if (path != null)
            {
                res.found = true;
                res.path = path;
                res.cost = path.Count - 1;
                res.visited = allVisited;
                res.steps = steps;
                res.note = "DFS(迭代加深) 在深度 " + depth + " 找到路径，长度 " + res.cost +
                           " 步，累计访问 " + allVisited.Count + " 次";
                return res;
            }
        }

        res.visited = allVisited;
        res.steps = steps;
        res.note = "DFS 未找到路径";
        return res;
    }

    // 深度受限 DFS：在指定深度内用显式栈找一条路径（不保证最短）。
    static List<Vector2Int> DepthLimitedDFS(Grid grid, Vector2Int start, Vector2Int goal,
        int limit, List<Vector2Int> visitedOut, ref int steps)
    {
        var parent = new Dictionary<Vector2Int, Vector2Int>();
        var visited = new HashSet<Vector2Int>();
        var stack = new Stack<(Vector2Int cell, int depth)>();

        stack.Push((start, 0));
        visited.Add(start);
        visitedOut.Add(start);
        steps++;

        while (stack.Count > 0)
        {
            var (cur, depth) = stack.Pop();
            if (cur == goal) return Reconstruct(parent, start, goal);
            if (depth >= limit) continue;

            var ns = Neighbors(grid, cur);
            for (int i = ns.Count - 1; i >= 0; i--) // 逆序压栈保证稳定遍历顺序
            {
                var n = ns[i];
                if (!visited.Add(n)) continue;
                parent[n] = cur;
                visitedOut.Add(n);
                steps++;
                stack.Push((n, depth + 1));
            }
        }
        return null;
    }

    // ============ Q-Learning：表格强化学习 ============

    public static PathResult QLearning(Grid grid, Vector2Int start, Vector2Int goal)
    {
        var res = new PathResult();
        int w = grid.width, h = grid.height;
        int maxEpisodes = 800;
        int maxStepsPerEp = w * h * 3;
        float alpha = 0.5f, gamma = 0.9f;
        float eps = 1.0f, epsMin = 0.05f;
        float epsDecay = (eps - epsMin) / maxEpisodes;
        var rng = new System.Random(12345);

        var Q = new float[w, h, 4];
        int episodesUsed = maxEpisodes;

        for (int e = 0; e < maxEpisodes; e++)
        {
            var s = start;
            for (int t = 0; t < maxStepsPerEp; t++)
            {
                // epsilon-greedy 选动作
                int a = (rng.NextDouble() < eps) ? rng.Next(4) : ArgMaxAction(Q, s);

                var ns = s + Dirs[a];
                float reward;
                if (!grid.IsWalkable(ns))
                {
                    ns = s;          // 撞墙原地
                    reward = -10f;
                }
                else if (ns == goal)
                {
                    reward = 100f;
                }
                else
                {
                    reward = -1f;
                }

                float maxQ = MaxQ(Q, ns);
                Q[s.x, s.y, a] += alpha * (reward + gamma * maxQ - Q[s.x, s.y, a]);

                s = ns;
                if (s == goal) break;
            }

            eps = System.Math.Max(epsMin, eps - epsDecay);

            // 周期性检查贪心策略是否已能到达终点，收敛则提前结束。
            if ((e + 1) % 25 == 0 && GreedyReachesGoal(Q, grid, start, goal, maxStepsPerEp))
            {
                episodesUsed = e + 1;
                break;
            }
        }

        // 学到的状态价值 V(s) = max_a Q(s,a)，用于热力图。
        var valueMap = new float[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (grid.IsWalkable(x, y))
                    valueMap[x, y] = MaxQ(Q, new Vector2Int(x, y));

        // 从起点按贪心策略走出一条路径。
        var path = new List<Vector2Int>();
        var onPath = new HashSet<Vector2Int>();
        var cur = start;
        path.Add(cur);
        onPath.Add(cur);
        bool reached = false;
        for (int t = 0; t < maxStepsPerEp; t++)
        {
            if (cur == goal) { reached = true; break; }
            var ns = cur + Dirs[ArgMaxAction(Q, cur)];
            if (!grid.IsWalkable(ns)) break;   // 策略指向墙（未充分收敛）
            cur = ns;
            if (!onPath.Add(cur)) break;       // 成环，终止
            path.Add(cur);
        }

        res.found = reached;
        res.path = path;
        res.cost = reached ? path.Count - 1 : 0f;
        res.visited = new List<Vector2Int>(); // RL 用热力图而非 visited
        res.valueMap = valueMap;
        res.steps = episodesUsed;
        res.note = reached
            ? "Q-Learning 训练 " + episodesUsed + " 回合收敛，贪心策略路径长度 " + res.cost + " 步"
            : "Q-Learning 训练 " + episodesUsed + " 回合，尚未收敛到终点";
        return res;
    }

    static bool GreedyReachesGoal(float[,,] Q, Grid grid, Vector2Int start, Vector2Int goal, int maxSteps)
    {
        var cur = start;
        var seen = new HashSet<Vector2Int>();
        for (int t = 0; t < maxSteps; t++)
        {
            if (cur == goal) return true;
            if (!seen.Add(cur)) return false;
            var ns = cur + Dirs[ArgMaxAction(Q, cur)];
            if (!grid.IsWalkable(ns)) return false;
            cur = ns;
        }
        return false;
    }

    static int ArgMaxAction(float[,,] Q, Vector2Int s)
    {
        int best = 0;
        float bestV = Q[s.x, s.y, 0];
        for (int a = 1; a < 4; a++)
            if (Q[s.x, s.y, a] > bestV) { bestV = Q[s.x, s.y, a]; best = a; }
        return best;
    }

    static float MaxQ(float[,,] Q, Vector2Int s)
    {
        float m = Q[s.x, s.y, 0];
        for (int a = 1; a < 4; a++)
            if (Q[s.x, s.y, a] > m) m = Q[s.x, s.y, a];
        return m;
    }
}
