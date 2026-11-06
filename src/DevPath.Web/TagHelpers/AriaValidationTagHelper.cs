using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DevPath.Web.TagHelpers;

/// <summary>
/// Acessibilidade dos erros de validação: em todo &lt;input asp-for&gt; / &lt;select asp-for&gt; / &lt;textarea asp-for&gt;
/// junta o aria-describedby já existente (ex.: dica permanente) com "{id}-error" só quando o campo
/// tem erro no ModelState — sem duplicar ids — e marca aria-invalid="true".
/// O &lt;span asp-validation-for&gt; correspondente deve ter id="{id}-error".
/// </summary>
[HtmlTargetElement("input", Attributes = ForAttributeName)]
[HtmlTargetElement("select", Attributes = ForAttributeName)]
[HtmlTargetElement("textarea", Attributes = ForAttributeName)]
public sealed class AriaValidationTagHelper : TagHelper
{
    private const string ForAttributeName = "asp-for";

    [HtmlAttributeName(ForAttributeName)]
    public ModelExpression For { get; set; } = default!;

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    // Roda depois dos tag helpers padrão (InputTagHelper etc.), que geram id/name.
    public override int Order => 1000;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (output.Attributes.TryGetAttribute("type", out var type) &&
            string.Equals(type.Value?.ToString(), "hidden", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fullName = ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(For.Name);
        var id = output.Attributes.TryGetAttribute("id", out var idAttr)
            ? idAttr.Value?.ToString() ?? TagBuilder.CreateSanitizedId(fullName, "_")
            : TagBuilder.CreateSanitizedId(fullName, "_");

        var parts = new List<string>();
        if (output.Attributes.TryGetAttribute("aria-describedby", out var existing) &&
            !string.IsNullOrWhiteSpace(existing.Value?.ToString()))
        {
            parts.AddRange(existing.Value.ToString()!
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var hasError = ViewContext.ViewData.ModelState.TryGetValue(fullName, out var entry) && entry.Errors.Count > 0;
        var errorId = $"{id}-error";
        if (hasError && !parts.Contains(errorId, StringComparer.Ordinal))
        {
            parts.Add(errorId);
        }

        if (parts.Count > 0)
        {
            output.Attributes.SetAttribute("aria-describedby", string.Join(" ", parts));
        }

        if (hasError)
        {
            output.Attributes.SetAttribute("aria-invalid", "true");
        }
    }
}
