using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;

/// <summary>
/// Fetches quiz questions from Firebase Realtime Database.
/// The React admin panel writes to the same database path, so new quizzes
/// appear in the Unity app automatically — no rebuild required.
///
/// Firebase path: /quizzes
///
/// Attach to a persistent GameObject in the Quiz scene.
/// Call LoadQuizzes() to fetch the latest questions on demand.
/// </summary>
public class QuizManager : MonoBehaviour
{
    // ── Firebase path ────────────────────────────────────────────────────────
    private const string QuizzesPath = "quizzes";

    // ── State ────────────────────────────────────────────────────────────────
    private DatabaseReference _dbRef;
    private bool _isFirebaseReady;

    /// <summary>All questions loaded from Firebase, keyed by Firebase push-ID.</summary>
    public Dictionary<string, QuizQuestion> Questions { get; private set; }
        = new Dictionary<string, QuizQuestion>();

    /// <summary>Raised after questions are successfully loaded or refreshed.</summary>
    public event System.Action OnQuestionsLoaded;

    /// <summary>Raised when a load fails, passing the error message.</summary>
    public event System.Action<string> OnLoadError;

    // ── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        InitialiseFirebase();
    }

    private void OnDestroy()
    {
        // Remove real-time listener to prevent memory leaks
        if (_dbRef != null)
            _dbRef.ValueChanged -= OnQuizzesValueChanged;
    }

    // ── Firebase Initialisation ──────────────────────────────────────────────

    private void InitialiseFirebase()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                // Enable offline persistence so quizzes work without internet
                // (uses the last-fetched data when offline)
                FirebaseDatabase.DefaultInstance.SetPersistenceEnabled(true);

                _dbRef = FirebaseDatabase.DefaultInstance
                                         .GetReference(QuizzesPath);

                _isFirebaseReady = true;
                Debug.Log("[QuizManager] Firebase ready. Loading quizzes...");

                // Subscribe to real-time updates:
                // This fires immediately with current data, then again
                // whenever the admin adds/edits/deletes a quiz.
                _dbRef.ValueChanged += OnQuizzesValueChanged;
            }
            else
            {
                Debug.LogError($"[QuizManager] Firebase unavailable: {task.Result}");
                OnLoadError?.Invoke("Could not connect to Firebase.");
            }
        });
    }

    // ── Real-time Listener ───────────────────────────────────────────────────

    /// <summary>
    /// Called automatically by Firebase whenever the /quizzes node changes.
    /// This is triggered both on first load and whenever the React admin
    /// adds, edits, or deletes a quiz — no app restart needed.
    /// </summary>
    private void OnQuizzesValueChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null)
        {
            Debug.LogError($"[QuizManager] Database error: {args.DatabaseError.Message}");
            OnLoadError?.Invoke(args.DatabaseError.Message);
            return;
        }

        ParseQuizzes(args.Snapshot);
    }

    // ── Manual Refresh ───────────────────────────────────────────────────────

    /// <summary>
    /// Manually fetches the latest quizzes once (one-shot, not real-time).
    /// Useful for a "Refresh" button in the UI.
    /// </summary>
    public void LoadQuizzes()
    {
        if (!_isFirebaseReady)
        {
            Debug.LogWarning("[QuizManager] Firebase not ready yet.");
            return;
        }

        _dbRef.GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                string error = task.Exception?.Message ?? "Unknown error";
                Debug.LogError($"[QuizManager] Failed to load quizzes: {error}");
                OnLoadError?.Invoke(error);
                return;
            }

            ParseQuizzes(task.Result);
        });
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    private void ParseQuizzes(DataSnapshot snapshot)
    {
        Questions.Clear();

        if (!snapshot.Exists)
        {
            Debug.Log("[QuizManager] No quizzes found in database.");
            OnQuestionsLoaded?.Invoke();
            return;
        }

        foreach (DataSnapshot child in snapshot.Children)
        {
            string key = child.Key;  // Firebase push-ID e.g. "-Nxyz123"

            string   question     = child.Child("question").Value?.ToString() ?? "";
            int      correctIndex = int.TryParse(child.Child("correctIndex").Value?.ToString(), out int ci) ? ci : 0;
            string   category     = child.Child("category").Value?.ToString() ?? "";
            string   imageUrl     = child.Child("imageUrl").Value?.ToString() ?? "";

            // Parse the options array
            var options = new List<string>();
            foreach (DataSnapshot opt in child.Child("options").Children)
                options.Add(opt.Value?.ToString() ?? "");

            Questions[key] = new QuizQuestion
            {
                question     = question,
                options      = options,
                correctIndex = correctIndex,
                category     = category,
                imageUrl     = imageUrl
            };
        }

        Debug.Log($"[QuizManager] Loaded {Questions.Count} quiz question(s) from Firebase.");
        OnQuestionsLoaded?.Invoke();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all questions filtered by category.
    /// Pass an empty string to get all questions.
    /// </summary>
    public List<QuizQuestion> GetByCategory(string category)
    {
        var result = new List<QuizQuestion>();

        foreach (var q in Questions.Values)
        {
            if (string.IsNullOrEmpty(category) || q.category == category)
                result.Add(q);
        }

        return result;
    }

    /// <summary>Returns all questions as a flat list in random order.</summary>
    public List<QuizQuestion> GetShuffled()
    {
        var list = new List<QuizQuestion>(Questions.Values);

        // Fisher-Yates shuffle
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }
}
