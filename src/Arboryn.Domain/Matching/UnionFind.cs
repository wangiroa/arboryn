namespace Arboryn.Domain.Matching;

/// <summary>
/// Structure union-find (disjoint set) avec compression de chemin, utilisée pour
/// agréger en composantes connexes les paires de fichiers jugées similaires
/// (détection floue, détection perceptuelle).
/// </summary>
public sealed class UnionFind
{
    private readonly int[] _parent;

    public UnionFind(int size)
    {
        _parent = new int[size];
        for (var i = 0; i < size; i++)
        {
            _parent[i] = i;
        }
    }

    public int Find(int x)
    {
        while (_parent[x] != x)
        {
            _parent[x] = _parent[_parent[x]];
            x = _parent[x];
        }

        return x;
    }

    public void Union(int a, int b) => _parent[Find(a)] = Find(b);
}
