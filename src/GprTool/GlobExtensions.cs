using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DotNet.Globbing;
using DotNet.Globbing.Token;

namespace GprTool
{
    public static class GlobExtensions
    {
        public static bool IsGlobPattern(this Glob glob)
        {
            return glob.Tokens.Any(x => !(x is PathSeparatorToken || x is LiteralToken));
        }

        public static string BuildBasePathFromGlob(this Glob glob, string baseDirectory)
        {
            if (glob == null) throw new ArgumentNullException(nameof(glob));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));

            var globTokens = glob.Tokens.ToList();
            var pathTokens = new List<IGlobToken>();

            for (var index = 0; index < globTokens.Count; index++)
            {
                var token = glob.Tokens[index];
                var tokenNext = index + 1 < globTokens.Count ? glob.Tokens[index + 1] : null;
                
                switch (token)
                {
                    case LiteralToken _:
                        pathTokens.Add(token);
                        if (tokenNext is AnyCharacterToken
                            || tokenNext is INegatableToken
                            && pathTokens.Any())
                        {
                            pathTokens.RemoveAt(pathTokens.Count - 1);
                            goto done;
                        }
                        continue;
                    case PathSeparatorToken _ when (tokenNext is LiteralToken):
                        pathTokens.Add(token);
                        continue;
                }

                goto done;
            }

            done:

            var pathStringBuilder = new StringBuilder();

            foreach (var token in pathTokens)
            {
                switch (token)
                {
                    case LiteralToken literalToken:
                        pathStringBuilder.Append(literalToken.Value);
                        break;
                    case PathSeparatorToken _: // xplat
                        pathStringBuilder.Append("/");
                        break;
                    default:
                        throw new NotSupportedException(token.GetType().FullName);
                }
            }

            var pathStr = pathStringBuilder.ToString();

            // Remove trailing backward/forward slash
            var lastAppendedGlobToken = pathTokens.LastOrDefault();
            if (lastAppendedGlobToken is PathSeparatorToken
                && pathStr.Sum(x => x == '/' ? 1 : 0) > 1)
            {
                pathStr = pathStr.Substring(0, pathStr.Length - 1);
            }

            return !Path.IsPathRooted(pathStr) ? Path.GetFullPath(pathStr, baseDirectory) : Path.GetFullPath(pathStr);
        }
    }
}
