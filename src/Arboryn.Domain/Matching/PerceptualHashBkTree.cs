using Arboryn.Domain.ValueObjects;

namespace Arboryn.Domain.Matching;

/// <summary>
/// Arbre BK (Burkhard-Keller) indexant des empreintes perceptuelles par distance de
/// Hamming. Permet une recherche par rayon (« toutes les empreintes à distance ≤ d »)
/// sans comparer le requête à chaque élément — essentiel pour le regroupement perceptuel
/// sur de grands catalogues.
///
/// Chaque nœud porte la liste des charges utiles (indices) partageant exactement la même
/// empreinte ; les empreintes distinctes sont rangées dans des sous-arbres clés par leur
/// distance au nœud.
/// </summary>
public sealed class PerceptualHashBkTree
{
    private sealed class Node
    {
        public Node(PerceptualHash hash) => Hash = hash;

        public PerceptualHash Hash { get; }

        public List<int> Payloads { get; } = new();

        public Dictionary<int, Node> Children { get; } = new();
    }

    private Node? _root;

    public int Count { get; private set; }

    /// <summary>Insère une empreinte associée à une charge utile (indice de l'élément).</summary>
    public void Add(PerceptualHash hash, int payload)
    {
        Count++;

        if (_root is null)
        {
            _root = new Node(hash);
            _root.Payloads.Add(payload);
            return;
        }

        var node = _root;
        while (true)
        {
            var distance = node.Hash.HammingDistance(hash);
            if (distance == 0)
            {
                node.Payloads.Add(payload);
                return;
            }

            if (node.Children.TryGetValue(distance, out var child))
            {
                node = child;
                continue;
            }

            var created = new Node(hash);
            created.Payloads.Add(payload);
            node.Children[distance] = created;
            return;
        }
    }

    /// <summary>Renvoie les charges utiles dont l'empreinte est à distance ≤ <paramref name="maxDistance"/>.</summary>
    public IReadOnlyList<int> Search(PerceptualHash query, int maxDistance)
    {
        var results = new List<int>();
        if (_root is null)
        {
            return results;
        }

        var stack = new Stack<Node>();
        stack.Push(_root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var distance = node.Hash.HammingDistance(query);
            if (distance <= maxDistance)
            {
                results.AddRange(node.Payloads);
            }

            // Inégalité triangulaire : seuls les enfants à clé dans [d-max, d+max] peuvent matcher.
            var low = distance - maxDistance;
            var high = distance + maxDistance;
            foreach (var (key, child) in node.Children)
            {
                if (key >= low && key <= high)
                {
                    stack.Push(child);
                }
            }
        }

        return results;
    }
}
