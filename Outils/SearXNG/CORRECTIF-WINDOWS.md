# SearXNG sous Windows — ce qu'il a fallu changer

SearXNG vise Linux. Il tourne ici, mais trois choses l'en empêchaient. Elles sont notées pour
qu'une mise à jour de la source ne les reperde pas en silence — c'est le mode de panne le plus
probable de cette installation.

## 1. `import pwd` — le seul import Unix de tout l'arbre

`searx/valkeydb.py` importait `pwd` sans condition. Le module n'existe pas sous Windows, et
l'import faisait échouer le démarrage **entier**, avant la première ligne de `webapp.py`.

Il ne sert qu'à nommer l'utilisateur dans **un** message d'erreur, celui d'une connexion Valkey
ratée — un chemin que cette installation n'atteint jamais, puisqu'aucune URL Valkey n'est
configurée.

L'import est devenu conditionnel, et son unique point d'usage protégé. **À réappliquer après toute
mise à jour de la source.** Le symptôme, sinon : `ModuleNotFoundError: No module named 'pwd'`.

## 2. `tzdata` — Windows n'a pas de base de fuseaux horaires

Linux en fournit une au système ; Windows non. Deux moteurs — `bilibili`, `torch` — appellent
`ZoneInfo("Asia/Shanghai")` au chargement et échouaient sur
`ZoneInfoNotFoundError`. SearXNG démarrait quand même, en écartant ces deux moteurs.

Corrigé en installant le paquet `tzdata` dans le venv. Il n'est pas dans `requirements.txt` parce
qu'il ne sert à rien sous Linux.

## 3. Quatre fichiers au nom illégal

`utils/templates/…/searxng.conf:socket` et trois autres portent un `:` dans leur nom, ce que NTFS
réserve aux flux de données alternatifs. `git clone` échoue dessus et laisse la copie incomplète —
c'est pourquoi la source vient d'une **archive** et non d'un clone, en excluant `utils/templates`.

Ce sont des gabarits de déploiement nginx, Apache et uwsgi. Aucun usage ici.

## Ce qui n'est PAS un correctif

- `limiter: false` — le limiteur protège une instance **publique** des abus. Celle-ci n'a qu'un
  client, sur la boucle locale : il ne ferait que refuser les recherches de l'utilisateur.
- `X-Forwarded-For nor X-Real-IP header is set!` au journal — bruit de la détection de robots, sans
  effet quand le limiteur est éteint.

## Le format JSON

`search.formats` doit lister `json`. SearXNG ne sert que du HTML par défaut, et le produit
interroge en JSON — `SearxngQuery` ajoute `&format=json`. Sans cette ligne, l'instance répond 403,
ce que `SearxngClient` sait nommer mais ne peut pas corriger. C'est la raison d'être de
`settings.yml`.

## Remonter une instance neuve

    Outils/Python/python.exe -m venv Outils/SearXNG/venv
    Outils/SearXNG/venv/Scripts/python.exe -m pip install -r Outils/SearXNG/source/requirements.txt tzdata

Puis copier `settings.exemple.yml` en `settings.yml` et y mettre un secret :

    Outils/SearXNG/venv/Scripts/python.exe -c "import secrets; print(secrets.token_hex(32))"

`settings.yml` n'est pas versionné : il porte ce secret.
