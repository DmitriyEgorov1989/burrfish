namespace Burrfish.Application.LeadQualification;

/// <summary>Предоставляет утверждённые шаблоны диалога.</summary>
public interface IDialogTemplateProvider
{
    /// <summary>Возвращает настроенные шаблоны.</summary>
    /// <returns>Шаблоны; никогда не возвращает <see langword="null"/>.</returns>
    DialogTemplates Get();
}
