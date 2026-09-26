using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Razor;

namespace ColdCeph.Control.Composition;

public sealed class SliceViewLocationExpander : IViewLocationExpander
{
    public void PopulateValues(ViewLocationExpanderContext context)
    {
    }

    public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
    {
        if (context.ActionContext.ActionDescriptor is ControllerActionDescriptor descriptor)
        {
            var ns = descriptor.ControllerTypeInfo.Namespace ?? "";
            const string marker = ".Features.";
            var index = ns.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                var slice = ns[(index + marker.Length)..].Split('.')[0];
                yield return $"/Features/{slice}/Views/{{0}}.cshtml";
            }
        }

        yield return "/Shared/Views/{0}.cshtml";
        foreach (var location in viewLocations)
            yield return location;
    }
}
