# gpr

A .NET Core tool for working with the GitHub Package Registry

```
Usage: gpr [options] [command]

Options:
  --help        Show help information
  -k|--api-key  The access token to use

Commands:
  delete        Delete package versions
  details       View package details
  encode        Encode PAT to prevent it from being automatically deleted by GitHub
  files         List files for a package
  list          List packages for user or org (viewer if not specified)
  push          Publish a package
  setApiKey     Set GitHub API key/personal access token

Run 'gpr [command] --help' for more information about a command.
```
