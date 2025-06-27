using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver.Resolvers;

namespace Kraftvaerk.Umbraco.Headless.CacheKeys.Backend.Services.CacheDependencySolver;
public class CacheKeyDependencyResolver : ICacheKeyDependencyResolver
{
    private readonly PickerDependencyResolver _pickerResolver;
    private readonly BlockDependencyResolver _blockResolver;
    private readonly RelationDependencyResolver _relationResolver;
    private readonly IContentService _contentService;

    public CacheKeyDependencyResolver(
        IRelationService relationService, IContentService contentService)
    {
        _pickerResolver = new PickerDependencyResolver();
        _relationResolver = new RelationDependencyResolver(relationService, contentService);
        _blockResolver = new BlockDependencyResolver(); // Pass self for recursion
        _contentService = contentService;
    }

    public IEnumerable<string> GetDependencies(IContent content)
    {
        var dependencies = new HashSet<string>
        {
            $"content-{content.Key}"
        };

        dependencies.UnionWith(_pickerResolver.GetPickerDependencies(content));
        dependencies.UnionWith(_blockResolver.GetBlockDependencies(content));
        dependencies.UnionWith(_relationResolver.GetRelationDependencies(content));

        if (content.HasProperty("childKeys"))
        {
            var includeChildren = content.GetValue<bool>("childKeys");

            if (includeChildren)
            {
                var children = _contentService.GetPagedChildren(content.Id, 0, 100, out long _);
                foreach (var child in children)
                {
                    if(child.Published)
                        dependencies.UnionWith(GetDependencies(child));
                }
            }

        }

        return dependencies;
    }
}
