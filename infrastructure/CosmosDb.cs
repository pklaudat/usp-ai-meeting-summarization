using System;
using Pulumi;
using System.Collections.Generic;
using Pulumi.AzureNative.Resources;
using Pulumi.AzureNative.Web;
using Pulumi.AzureNative.DocumentDB;
using Pulumi.AzureNative.Web.Inputs;
using Pulumi.AzureNative.Authorization;
using Inputs = Pulumi.AzureNative.DocumentDB.Inputs;
using ManagedServiceIdentityType = Pulumi.AzureNative.Web.ManagedServiceIdentityType;
using System.Linq;

namespace UspMeetingSummz
{
    class CosmosDb
    {
        private string _databaseName;
        private ResourceGroup _resourceGroup;
        private string _name;
        private readonly string _location;
        private readonly string _env;

        public Output<string> DatabaseName { get; private set; }
        public Output<string> ResourceGroupId { get; private set; }

        public CosmosDb(string name, string location, string env, ResourceGroup resourceGroup)
        {
            this._resourceGroup = resourceGroup;
            this._databaseName = $"cdb-{name}-{location}-{env}";
            this._location = location;
            this._env = env;
            DatabaseName = Output.Create(_databaseName);
            ResourceGroupId = resourceGroup.Id;
            CreateDatabase(_databaseName, location);
        }
        private void OauthAccessToDb(WebApp function, Output<string> storageAccountId)
        {
            var builtinRolesIds = new List<string>
            {
                "b7e6dc6d-f1e8-4753-8033-0f276bb0955b",
                "974c5e8b-45b9-4653-ba55-5f855dd0fb88",
                "0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3",
                "17d1049b-9a84-46fb-8f53-869881c3d3ab",
                "69566ab7-960f-475b-8e7c-b3118f30c6bd"
            };

            foreach (var roleId in builtinRolesIds)
            {
                var roleAssignment = new RoleAssignment($"{_name}-{roleId}", new RoleAssignmentArgs
                {
                    PrincipalId = function.Identity.Apply(identity => identity.PrincipalId),
                    RoleDefinitionId = $"/providers/Microsoft.Authorization/roleDefinitions/{roleId}",
                    Scope = storageAccountId,
                    PrincipalType = "ServicePrincipal"
                });
            }

        }

        public void CreateDatabase(string databaseName, string location)
        {
            new DatabaseAccount(databaseName, new DatabaseAccountArgs
            {
                AccountName = databaseName,
                Location = location,
                DatabaseAccountOfferType = DatabaseAccountOfferType.Standard,
                ResourceGroupName = _resourceGroup.Name,
                CreateMode = CreateMode.Default,
                MinimalTlsVersion = "Tls12",
                Locations = new []
                {
                    new Inputs.LocationArgs
                    {
                        FailoverPriority = 0,
                        IsZoneRedundant = false,
                        LocationName = location
                    }
                } 

            });
        }

        public Output<string> GetResourceGroupId()
        {
            return Output.Format($"");
        }
    }
}
