using System;
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

        public static string BuildBasePathFromGlob(this Glob glob, string fallbackPath = null)
        {
            if (glob == null) throw new ArgumentNullException(nameof(glob));

            var tokensLength = glob.Tokens.Length;

            var path = new StringBuilder();

            for (var index = 0; index < tokensLength; index++)
            {
                var token = glob.Tokens[index];
                var tokenNext = index + 1 < tokensLength ? glob.Tokens[index + 1] : null;
                var tokenPrevious = index - 1 >= 0 ? glob.Tokens[index - 1] : null;
                var tokenPreviousPrevious = index - 2 >= 0 ? glob.Tokens[index - 2] : null;

                switch (token)
                {
                    case PathSeparatorToken pathSeparatorToken when(tokenNext is LiteralToken):
                        path.Append(pathSeparatorToken.Value);
                        break;
                    case LiteralToken literalToken:

                        if (tokenPrevious is WildcardToken
                            || tokenPreviousPrevious is WildcardDirectoryToken)
                        {
                            goto done;
                        }

                        path.Append(literalToken.Value);

                        if (tokenNext is WildcardToken 
                            || tokenNext is WildcardDirectoryToken)
                        {
                            goto done;
                        }

                        break;
                }
            }

            done:

            var pathStr = path.ToString();
            if (fallbackPath != null && string.IsNullOrWhiteSpace(pathStr))
            {
                return fallbackPath;
            }

            return pathStr;
        }
    }
}
