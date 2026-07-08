using System.Collections.Generic;

/// <summary>
/// Mirrors the quiz structure stored in Firebase Realtime Database.
/// Both Unity and the React admin system must use this same schema.
///
/// Firebase path: /quizzes/{quizId}
/// </summary>
[System.Serializable]
public class QuizQuestion
{
    /// <summary>The question text displayed to the user.</summary>
    public string question;

    /// <summary>List of answer choices (usually 4).</summary>
    public List<string> options;

    /// <summary>Zero-based index of the correct option in the options list.</summary>
    public int correctIndex;

    /// <summary>Category label e.g. "Tree Species", "Growth", "General".</summary>
    public string category;

    /// <summary>Optional URL to an image shown with the question.</summary>
    public string imageUrl;
}
