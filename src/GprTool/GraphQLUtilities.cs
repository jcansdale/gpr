using System;
using System.Threading.Tasks;
using Octokit.GraphQL;
using Octokit.GraphQL.Core;
using Octokit.GraphQL.Model;

namespace GprTool
{
    public class GraphQLUtilities
    {

        public static async Task<PackageConnection> FindPackageConnection(IConnection connection, string owner, int first = 100)
        {
            if(owner == null)
            {
                return new Query().Viewer.Packages(first: first);
            }

            try
            {
                var query = new Query().User(owner).Packages().Select(p => p.TotalCount).Compile();
                var total = await connection.Run(query);
                return new Query().User(owner).Packages(first: first);
            }
            catch (GraphQLException e) when (e.Message.StartsWith("Could not resolve to a "))
            {
                // continue looking
            }
            catch (GraphQLException e)
            {
                throw new ApplicationException(e.Message, e);
            }            

            try
            {
                var query = new Query().Organization(owner).Packages().Select(p => p.TotalCount).Compile();
                var total = await connection.Run(query);
                return new Query().Organization(owner).Packages(first: first);
            }
            catch (GraphQLException e) when (e.Message.StartsWith("Could not resolve to a "))
            {
                // continue looking
            }
            catch (GraphQLException e)
            {
                throw new ApplicationException(e.Message, e);
            }            

            return null;
        }
    }
}