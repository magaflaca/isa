# Contributing

Run `python isa.py setup` first (see the README). After that `src/` is the full project and you work in it like in
any other C# project.

## Where your change goes

- **New code goes in `src/IsaClient.*/`.** These files are tracked by git as they are.
- **Game files (`src/Terraria*/` and the rest of `src/`) are not tracked.** Git only tracks the diff, in `patches/`.
  Keep changes there small: a one-line call into an `IsaClient.*` class is the usual shape.
- Need a new member on a game class (`Player`, `Main`...)? Put it in `src/IsaClient.Partials/`, in the partial half of
  that class, not in the game file.

`ARCHITECTURE.md` has the full rules.

## Changing a game file

Edit it in `src/`, build, test. When you're done:

```
python isa.py status     # what you changed
python isa.py patches    # rewrites patches/ from src/
```

Commit the `.patch` files. Don't edit them by hand.

After a `git pull`, run `python isa.py setup` again. It updates the game files from the new patches and leaves the
ones you edited alone (`setup --reset` drops your edits).

## Pull requests

- One thing per PR.
- It has to build: `dotnet build src/Terraria.csproj -c Release`.
- Say how you tested it in game.
- UI text, chat messages and code are in English.
- Don't add API keys, tokens or webhooks.

PRs are not merged here with the merge button. An accepted PR is applied to the private repo and shows up in this one
with the next release.

## Translations

Languages live in https://github.com/magaflaca/isa-l10n, not here.
