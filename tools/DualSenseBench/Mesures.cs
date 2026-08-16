using System.Text;

namespace DualSenseBench;

/// <summary>Une trame telle qu'elle est arrivée : quand, et quoi.</summary>
public sealed record Trame(long Microsecondes, byte[] Octets);

/// <summary>Ce qu'une commande a fait bouger sur un octet.</summary>
/// <param name="Octet">L'indice de l'octet.</param>
/// <param name="Avant">Sa valeur juste avant le pic.</param>
/// <param name="Extreme">La valeur la plus éloignée atteinte pendant l'appui.</param>
/// <param name="BitsAllumes">Les bits passés de 0 à 1. Un bouton est là-dedans, un axe n'y est pas.</param>
public sealed record Effet(int Octet, byte Avant, byte Extreme, byte BitsAllumes)
{
    public int Ampleur => Math.Abs(Extreme - Avant);

    public string Lisible => BitsAllumes != 0 && Ampleur < 64
        ? $"octet {Octet} · bit 0x{BitsAllumes:X2}"
        : $"octet {Octet} · {Avant} → {Extreme}";
}

/// <summary>
/// Détecte les pics : un octet stable qui change brusquement.
/// </summary>
/// <remarks>
/// Un appui est un pic, et rien d'autre ne l'est. Une dérive de stick monte de quelques crans en
/// quelques secondes ; un compteur de trames tourne à chaque trame ; ni l'un ni l'autre ne saute.
/// C'est ce qui distingue une commande de ce que la manette fait toute seule, et c'est la seule
/// chose qu'il fallait regarder.
///
/// <para>
/// Chaque octet porte sa propre fenêtre des cent dernières millisecondes, et c'est à elle qu'on
/// compare. Une fenêtre glissante plutôt qu'un repos relevé une fois pour toutes : la dérive entre
/// dedans au fil de l'eau, donc elle ne déclenche jamais, et il n'y a plus aucune phase « ne touche
/// à rien » à respecter.
/// </para>
///
/// <para>
/// Les trois natures d'octet ont été mesurées sur une vraie manette, et chacune a sa règle. Les
/// confondre sous un seuil unique donne soit un instrument aveugle aux boutons — L1, R1 et L2 valent
/// 1, 2 et 4, moins qu'une marge anti-bruit — soit un instrument qui voit du mouvement en permanence
/// sur une manette posée sur la table. Ce projet a produit les deux.
/// </para>
/// </remarks>
public sealed class Detecteur
{
    /// <summary>Cent millisecondes à six cents trames par seconde.</summary>
    private const int Fenetre = 60;

    private readonly List<List<byte>> _histoires = [];
    private readonly List<bool> _amorce = [];
    private readonly List<bool> _enPic = [];
    private readonly List<byte> _base = [];
    private readonly List<int> _tenue = [];
    private readonly List<byte> _baseBits = [];
    private readonly List<int[]> _tenueBits = [];
    private readonly List<byte> _avant = [];
    private readonly List<byte> _extreme = [];
    private readonly List<int> _ampleur = [];
    private readonly List<byte> _bits = [];

    /// <summary>Trente trames à six cents par seconde : cinquante millisecondes tenues.</summary>
    private const int Tenue = 30;

    /// <summary>Au-delà, l'écart n'est plus un appui mais la nouvelle normale de l'octet.</summary>
    private const int Derive = 2000;

    /// <summary>
    /// Une trame de plus.
    /// </summary>
    /// <remarks>
    /// Un appui revient. C'est la seule chose qui sépare vraiment une commande de ce que la manette
    /// fait toute seule : un bouton relâché retrouve exactement la valeur qu'il avait, un stick
    /// ramené au centre aussi, tandis qu'une dérive part et ne revient pas, et qu'un compteur de
    /// trames ne se pose jamais. Les seuils, les marges et les repos figés ont tous échoué avant
    /// cette règle-là — chacun laissait passer soit les boutons dont le bit vaut 1, soit le bruit.
    ///
    /// <para>
    /// Trois conditions, donc : l'octet quitte sa valeur de base, il y reste au moins cinquante
    /// millisecondes — ce qu'un tremblement analogique ne fait jamais — et il revient. C'est au
    /// retour que l'appui est enregistré, avec l'extrême atteint entre-temps. Un écart qui ne revient
    /// pas au bout de trois secondes est une dérive : sa valeur devient la nouvelle base, sans rien
    /// enregistrer.
    /// </para>
    /// </remarks>
    public void Observer(byte[] trame)
    {
        Etendre(trame.Length);

        for (var i = 0; i < trame.Length; i++)
        {
            var v = trame[i];
            var histoire = _histoires[i];

            histoire.Add(v);

            if (histoire.Count > Fenetre)
            {
                histoire.RemoveAt(0);
            }

            if (!_amorce[i])
            {
                _amorce[i] = true;
                _base[i] = v;
                _baseBits[i] = v;
                continue;
            }

            // Un octet qui prend huit valeurs ou plus dans la fenêtre est un compteur. Il n'a pas de
            // base, donc la règle du retour ne s'y applique pas — mais ses bits, eux, l'ont. Sur une
            // DualSense en Bluetooth, le bouton PS et le contact du pavé tactile logent dans les deux
            // bits bas du même octet que le compteur de trames : ignorer l'octet entier, c'est les
            // rendre impossibles à enregistrer. Les bits du compteur changent à chaque trame et ne
            // tiennent jamais cinquante millisecondes ; ils s'éliminent d'eux-mêmes.
            if (Distinctes(histoire) >= 8)
            {
                ObserverBits(i, v);
                continue;
            }

            if (v == _base[i])
            {
                if (_enPic[i] && _tenue[i] >= Tenue && Math.Abs(_extreme[i] - _base[i]) > _ampleur[i])
                {
                    _avant[i] = _base[i];
                    _ampleur[i] = Math.Abs(_extreme[i] - _base[i]);
                    _bits[i] |= (byte)(_extreme[i] & ~_base[i]);
                }

                _enPic[i] = false;
                _tenue[i] = 0;
                continue;
            }

            if (!_enPic[i])
            {
                _enPic[i] = true;
                _tenue[i] = 1;
                _extreme[i] = v;
                continue;
            }

            _tenue[i]++;

            if (Math.Abs(v - _base[i]) > Math.Abs(_extreme[i] - _base[i]))
            {
                _extreme[i] = v;
            }

            if (_tenue[i] > Derive)
            {
                _base[i] = v;
                _enPic[i] = false;
                _tenue[i] = 0;
            }
        }
    }

    /// <summary>La même règle du retour, bit par bit, pour les octets partagés avec un compteur.</summary>
    private void ObserverBits(int octet, byte valeur)
    {
        for (var bit = 0; bit < 8; bit++)
        {
            var masque = (byte)(1 << bit);
            var courant = (valeur & masque) != 0;
            var repos = (_baseBits[octet] & masque) != 0;

            if (courant == repos)
            {
                if (_tenueBits[octet][bit] >= Tenue)
                {
                    _bits[octet] |= masque;
                    _avant[octet] = _baseBits[octet];
                    _extreme[octet] = (byte)(_baseBits[octet] ^ masque);
                    _ampleur[octet] = Math.Max(_ampleur[octet], 1);
                }

                _tenueBits[octet][bit] = 0;
                continue;
            }

            _tenueBits[octet][bit]++;

            if (_tenueBits[octet][bit] > Derive)
            {
                _baseBits[octet] ^= masque;
                _tenueBits[octet][bit] = 0;
            }
        }
    }

    private static int Distinctes(List<byte> histoire)
    {
        var vues = 0UL;
        var distinctes = 0;

        foreach (var v in histoire)
        {
            if ((vues & (1UL << (v & 0x3F))) == 0)
            {
                vues |= 1UL << (v & 0x3F);

                if (++distinctes >= 8)
                {
                    return distinctes;
                }
            }
        }

        return distinctes;
    }

    private void Etendre(int longueur)
    {
        while (_histoires.Count < longueur)
        {
            _histoires.Add([]);
            _amorce.Add(false);
            _enPic.Add(false);
            _base.Add(0);
            _tenue.Add(0);
            _baseBits.Add(0);
            _tenueBits.Add(new int[8]);
            _avant.Add(0);
            _extreme.Add(0);
            _ampleur.Add(0);
            _bits.Add(0);
        }
    }

    /// <summary>Ce qui a sauté depuis la dernière remise à zéro, le plus ample d'abord.</summary>
    public List<Effet> Effets()
    {
        var effets = new List<Effet>();

        for (var i = 0; i < _histoires.Count; i++)
        {
            if (_ampleur[i] > 0)
            {
                effets.Add(new Effet(i, _avant[i], _extreme[i], _bits[i]));
            }
        }

        effets.Sort((a, b) => b.Ampleur.CompareTo(a.Ampleur));
        return effets;
    }

    /// <summary>Oublie les pics retenus, garde les fenêtres : la manette n'a pas changé d'état.</summary>
    public void Reinitialiser()
    {
        for (var i = 0; i < _histoires.Count; i++)
        {
            _ampleur[i] = 0;
            _bits[i] = 0;
            _extreme[i] = 0;
            _avant[i] = 0;
        }
    }
}

/// <summary>Ce qu'une commande a donné, telle que l'opérateur l'a validée.</summary>
public sealed record Releve(Controle Controle, List<Effet> Effets, bool Ignoree)
{
    public string Lisible => Ignoree
        ? "passée"
        : Effets.Count == 0 ? "**rien détecté**" : string.Join(" · ", Effets.Take(2).Select(e => e.Lisible));
}

/// <summary>
/// Le fichier de correspondance, écrit au fur et à mesure.
/// </summary>
/// <remarks>
/// Chaque commande validée part sur le disque tout de suite, pas à la fermeture : une séance dont la
/// fenêtre se ferme au dixième bouton garde ses dix premiers.
/// </remarks>
public static class Rapport
{
    public static string Entete(string informations)
    {
        var sortie = new StringBuilder();
        sortie.AppendLine("# Correspondance des commandes");
        sortie.AppendLine();
        sortie.Append(informations);
        sortie.AppendLine();
        sortie.AppendLine("| Commande | Ce qui bouge dans le rapport | tout ce qui a sauté |");
        sortie.AppendLine("|---|---|---|");
        return sortie.ToString();
    }

    public static string Ligne(Releve releve)
    {
        var tout = releve.Effets.Count == 0
            ? "—"
            : string.Join(", ", releve.Effets.Select(e => $"o{e.Octet}:{e.Avant}→{e.Extreme}"));

        return $"| {releve.Controle.Nom} | {releve.Lisible} | {tout} |";
    }

    public static string Pied()
        => Environment.NewLine
           + "Un appui est un pic : un octet stable qui change brusquement. Une dérive de stick et un "
           + "compteur de trames n'en sont pas, et n'apparaissent donc pas ici. « rien détecté » veut "
           + "dire qu'aucun octet n'a sauté : la commande n'existe pas dans cette forme de rapport."
           + Environment.NewLine;
}
