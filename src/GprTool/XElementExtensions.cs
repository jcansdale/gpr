using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace GprTool
{
    public static class XElementExtensions
    {
        public static XElement Single([NotNull] this XDocument xDocument, [NotNull] XName name, bool ignoreCase = true, bool ignoreNamespace = true)
        {
            if (xDocument == null) throw new ArgumentNullException(nameof(xDocument));
            if (name == null) throw new ArgumentNullException(nameof(name));
            return xDocument.Descendants().SingleOrDefault(name, ignoreCase, ignoreNamespace, true);
        }

        public static XElement Single([NotNull] this XElement xElement, [NotNull] XName name, bool ignoreCase = true, bool ignoreNamespace = true)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            if (name == null) throw new ArgumentNullException(nameof(name));
            return xElement.Descendants().SingleOrDefault(name, ignoreCase, ignoreNamespace, true);
        }

        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public static XElement SingleOrDefault([NotNull] this XDocument xDocument, [NotNull] XName name, bool ignoreCase = true, bool ignoreNamespace = true)
        {
            if (xDocument == null) throw new ArgumentNullException(nameof(xDocument));
            if (name == null) throw new ArgumentNullException(nameof(name));
            return xDocument.Descendants().SingleOrDefault(name, ignoreCase, ignoreNamespace);
        }

        public static XElement SingleOrDefault([NotNull] this XElement xElement, [NotNull] XName name, bool ignoreCase = true, bool ignoreNamespace = true)
        {
            if (xElement == null) throw new ArgumentNullException(nameof(xElement));
            if (name == null) throw new ArgumentNullException(nameof(name));
            return xElement.Descendants().SingleOrDefault(name, ignoreCase, ignoreNamespace);
        }

        public static XElement SingleOrDefault([NotNull] this IEnumerable<XElement> xElements, [NotNull] XName name, bool ignoreCase = true, bool ignoreNamespace = true, bool throwifNotFound = false)
        {
            if (xElements == null) throw new ArgumentNullException(nameof(xElements));
            if (name == null) throw new ArgumentNullException(nameof(name));
            foreach (var node in xElements)
            {
                var comperator = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!string.Equals(node.Name.LocalName, name.LocalName, comperator))
                {
                    continue;
                }

                if (!ignoreNamespace && !string.Equals(node.Name.NamespaceName, name.NamespaceName, comperator))
                {
                    continue;
                }

                return node;
            }

            if (throwifNotFound)
            {
                throw new Exception($"The required element '{name}' is missing");
            }

            return null;
        }
    }
}
