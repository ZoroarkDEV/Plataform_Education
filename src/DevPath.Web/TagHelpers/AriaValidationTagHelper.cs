using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DevPath.Web.TagHelpers;

/// <summary>
/// Acessibilidade dos erros de validação: em todo &lt;input asp-for&gt; / &lt;select asp-for&gt; / &lt;textarea asp-for&gt;
/// adiciona aria-describedby="{id}-error" (o &lt;span asp-validation-for&gt; correspondente deve ter esse id)
/// e, quando o ModelState tem erro para o campo, aria-invalid="true".
/// Assim o leitor de tela anuncia a mensagem de erro junto com o campo.
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

        var describedBy = $"{id}-error";
        if (output.Attributes.TryGetAttribute("aria-describedby", out var existing) &&
            !string.IsNullOrWhiteSpace(existing.Value?.ToString()))
        {
            describedBy = $"{existing.Value} {describedBy}";
        }
        output.Attributes.SetAttribute("aria-describedby", describedBy);

        if (ViewContext.ViewData.ModelState.TryGetValue(fullName, out var entry) && entry.Errors.Count > 0)
        {
            output.Attributes.SetAttribute("aria-invalid", "true");
        }
    }
}
