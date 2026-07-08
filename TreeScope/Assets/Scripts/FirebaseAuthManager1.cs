using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;

/// <summary>
/// Manages Firebase Authentication for TreeScope.
/// Attach this script to a persistent GameObject in the scene (e.g. "AuthManager").
///
/// Inspector wiring:
///   Login Panel      → loginPanel          (the LoginPanel GameObject)
///   Register Panel   → registerPanel       (the RegisterPanel GameObject)
///
///   Login inputs     → emailInput, passwordInput
///   Login buttons    → loginButton, signupButton (navigates to register panel)
///
///   Register inputs  → registerEmailInput, registerPasswordInput, confirmPasswordInput
///   Register buttons → registerButton, backToLoginButton
///
///   Feedback         → statusText (a TMP_Text visible in both panels, or one per panel)
///   Loading overlay  → loadingOverlay (optional CanvasGroup that dims the UI while working)
/// </summary>
public class FirebaseAuthManager : MonoBehaviour
{
    // ── Panel References ────────────────────────────────────────────────────
    [Header("Panels")]
    [Tooltip("The login panel root GameObject.")]
    [SerializeField] private GameObject loginPanel;

    [Tooltip("The registration panel root GameObject.")]
    [SerializeField] private GameObject registerPanel;

    // ── Login UI ────────────────────────────────────────────────────────────
    [Header("Login UI")]
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Button loginButton;

    /// <summary>Button that navigates from the Login panel to the Register panel (for new users).</summary>
    [SerializeField] private Button registerButton;   // "RegisterButton" in LoginPanel

    // ── Register UI ─────────────────────────────────────────────────────────
    [Header("Register UI")]
    [SerializeField] private TMP_InputField registerEmailInput;
    [SerializeField] private TMP_InputField registerPasswordInput;
    [SerializeField] private TMP_InputField confirmPasswordInput;

    /// <summary>Button that submits the registration form.</summary>
    [SerializeField] private Button signupButton;     // "SignupButton" in RegisterPanel

    /// <summary>Button that navigates back from the Register panel to the Login panel.</summary>
    [SerializeField] private Button backToLoginButton;

    // ── Feedback ────────────────────────────────────────────────────────────
    [Header("Feedback")]
    [Tooltip("TMP_Text used to display status / error messages to the user.")]
    [SerializeField] private TMP_Text statusText;

    [Tooltip("Optional CanvasGroup overlay shown while an async operation is in progress.")]
    [SerializeField] private CanvasGroup loadingOverlay;

    // ── Colours for status messages ─────────────────────────────────────────
    [Header("Status Colours")]
    [SerializeField] private Color successColour = new Color(0.18f, 0.80f, 0.44f);
    [SerializeField] private Color errorColour   = new Color(0.91f, 0.30f, 0.24f);
    [SerializeField] private Color infoColour    = new Color(0.90f, 0.90f, 0.90f);

    // ── Internal State ───────────────────────────────────────────────────────

    /// <summary>
    /// Tracks which auth action the user explicitly triggered.
    /// None    = startup / persisted session restore (no navigation).
    /// Login   = user clicked Login   → navigate to HomePage on success.
    /// Register = user clicked SignUp → return to LoginPanel on success.
    /// </summary>
    private enum PendingAuth { None, Login, Register }
    private PendingAuth _pendingAuth = PendingAuth.None;

    private FirebaseAuth _auth;
    private FirebaseUser _currentUser;
    private bool _isFirebaseReady;

    // ── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // Start on the Login panel
        ShowLoginPanel();

        // Wire up button listeners
        loginButton?.onClick.AddListener(OnLoginClicked);
        registerButton?.onClick.AddListener(OnRegisterNavigateClicked);  // navigate → RegisterPanel
        signupButton?.onClick.AddListener(OnSignupClicked);              // submit registration
        backToLoginButton?.onClick.AddListener(OnBackToLoginClicked);

        // Disable interactive elements until Firebase is ready
        SetInteractable(false);
    }

    private void Start()
    {
        SetStatus("Initialising Firebase...", infoColour);
        InitialiseFirebase();
    }

    private void OnDestroy()
    {
        // Unsubscribe from auth state changes to prevent memory leaks
        if (_auth != null)
            _auth.StateChanged -= OnAuthStateChanged;
    }

    // ── Firebase Initialisation ──────────────────────────────────────────────

    private void InitialiseFirebase()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            DependencyStatus status = task.Result;

            if (status == DependencyStatus.Available)
            {
                _auth = FirebaseAuth.DefaultInstance;
                _auth.StateChanged += OnAuthStateChanged;

                _isFirebaseReady = true;
                SetInteractable(true);
                SetStatus("Ready. Please sign in.", infoColour);

                Debug.Log("[FirebaseAuthManager] Firebase initialised successfully.");
            }
            else
            {
                _isFirebaseReady = false;
                SetStatus($"Firebase unavailable: {status}", errorColour);
                Debug.LogError($"[FirebaseAuthManager] Firebase dependency error: {status}");
            }
        });
    }

    // ── Auth State Observer ──────────────────────────────────────────────────

    private void OnAuthStateChanged(object sender, EventArgs e)
    {
        FirebaseUser newUser = _auth?.CurrentUser;

        if (_currentUser == null && newUser != null)
        {
            _currentUser = newUser;
            Debug.Log($"[FirebaseAuthManager] Signed in as: {_currentUser.Email}");

            if (_pendingAuth == PendingAuth.Login)
            {
                // User explicitly logged in → go to HomePage
                _pendingAuth = PendingAuth.None;
                OnLoginSuccess(_currentUser);
            }
            else
            {
                // Persisted session restore OR register flow (handled in RegisterRoutine)
                Debug.Log("[FirebaseAuthManager] Auth state changed — no navigation triggered.");
            }
        }
        else if (_currentUser != null && newUser == null)
        {
            _currentUser = null;
            Debug.Log("[FirebaseAuthManager] Signed out.");
        }
    }

    // ── Button Handlers ──────────────────────────────────────────────────────

    private void OnLoginClicked()
    {
        if (!_isFirebaseReady) return;

        string email    = emailInput?.text.Trim() ?? string.Empty;
        string password = passwordInput?.text ?? string.Empty;

        if (!ValidateEmail(email))
        {
            SetStatus("Please enter a valid email address.", errorColour);
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            SetStatus("Password cannot be empty.", errorColour);
            return;
        }

        StartCoroutine(LoginRoutine(email, password));
    }

    /// <summary>Called by RegisterButton (LoginPanel) — navigates to the Register panel.</summary>
    private void OnRegisterNavigateClicked() => ShowRegisterPanel();

    /// <summary>Called by SignupButton (RegisterPanel) — submits the registration form.</summary>
    private void OnSignupClicked()
    {
        if (!_isFirebaseReady) return;

        string email    = registerEmailInput?.text.Trim() ?? string.Empty;
        string password = registerPasswordInput?.text ?? string.Empty;
        string confirm  = confirmPasswordInput?.text ?? string.Empty;

        if (!ValidateEmail(email))
        {
            SetStatus("Please enter a valid email address.", errorColour);
            return;
        }

        if (password.Length < 6)
        {
            SetStatus("Password must be at least 6 characters.", errorColour);
            return;
        }

        if (password != confirm)
        {
            SetStatus("Passwords do not match.", errorColour);
            return;
        }

        StartCoroutine(RegisterRoutine(email, password));
    }

    private void OnBackToLoginClicked() => ShowLoginPanel();

    // ── Coroutines ───────────────────────────────────────────────────────────

    private IEnumerator LoginRoutine(string email, string password)
    {
        SetLoading(true);
        SetStatus("Signing in...", infoColour);
        _pendingAuth = PendingAuth.Login;

        // ── Handle already-active session ────────────────────────────────────
        // Calling SignInWithEmailAndPasswordAsync while a Firebase session is
        // already active throws "An internal error has occurred" on some SDK
        // versions. We handle both cases explicitly before making the call.
        if (_auth.CurrentUser != null)
        {
            if (_auth.CurrentUser.Email.Equals(email, System.StringComparison.OrdinalIgnoreCase))
            {
                // Same account already signed in — skip the Firebase call
                // and navigate directly. No network request needed.
                Debug.Log("[FirebaseAuthManager] Same user already signed in — navigating directly.");
                SetLoading(false);
                SetStatus("Signed in successfully!", successColour);
                _pendingAuth = PendingAuth.None;
                OnLoginSuccess(_auth.CurrentUser);
                yield break;
            }
            else
            {
                // Different account — sign out the current session first so
                // Firebase reaches a clean state before re-authenticating.
                Debug.Log("[FirebaseAuthManager] Different user detected — signing out current session.");
                _auth.SignOut();
                yield return new WaitForSeconds(0.2f);  // brief wait for sign-out to settle
            }
        }

        // ── Fresh sign-in ────────────────────────────────────────────────────
        var loginTask = _auth.SignInWithEmailAndPasswordAsync(email, password);

        yield return new WaitUntil(() => loginTask.IsCompleted);

        SetLoading(false);

        if (loginTask.IsCanceled || loginTask.IsFaulted)
        {
            _pendingAuth = PendingAuth.None;  // reset on failure
            string message = GetFirebaseErrorMessage(loginTask.Exception);
            SetStatus(message, errorColour);
            Debug.LogWarning($"[FirebaseAuthManager] Login failed: {loginTask.Exception}");
        }
        else
        {
            SetStatus("Signed in successfully!", successColour);

            // Navigate directly here as a fallback.
            // When the user was ALREADY signed in with the same account
            // (Firebase restored a persisted session), OnAuthStateChanged
            // does NOT fire on re-login because the auth state didn't change.
            // The _pendingAuth guard prevents double-navigation if
            // OnAuthStateChanged DID fire and already handled it.
            if (_pendingAuth == PendingAuth.Login)
            {
                _pendingAuth = PendingAuth.None;
                OnLoginSuccess(_auth.CurrentUser);
            }
        }
    }

    private IEnumerator RegisterRoutine(string email, string password)
    {
        SetLoading(true);
        SetStatus("Creating account...", infoColour);
        _pendingAuth = PendingAuth.Register;

        var registerTask = _auth.CreateUserWithEmailAndPasswordAsync(email, password);

        yield return new WaitUntil(() => registerTask.IsCompleted);

        SetLoading(false);

        if (registerTask.IsCanceled || registerTask.IsFaulted)
        {
            _pendingAuth = PendingAuth.None;  // reset on failure
            string message = GetFirebaseErrorMessage(registerTask.Exception);
            SetStatus(message, errorColour);
            Debug.LogWarning($"[FirebaseAuthManager] Registration failed: {registerTask.Exception}");
        }
        else
        {
            Debug.Log($"[FirebaseAuthManager] Registered: {registerTask.Result?.User?.Email}");

            // Firebase auto-signs the user in after registration.
            // We sign them out and redirect to LoginPanel so they log in explicitly.
            _pendingAuth = PendingAuth.None;
            _auth.SignOut();
            ShowLoginPanel();
            SetStatus("Account created! Please sign in.", successColour);
        }
    }

    // ── Post-Authentication ──────────────────────────────────────────────────

    /// <summary>
    /// Called when the user explicitly logs in. Navigates to the HomePage scene.
    /// </summary>
    private void OnLoginSuccess(FirebaseUser user)
    {
        Debug.Log($"[FirebaseAuthManager] Welcome, {user.Email}! Navigating to HomePage.");
        SceneManager.LoadScene("HomePage");
    }

    // ── Panel Helpers ────────────────────────────────────────────────────────

    private void ShowLoginPanel()
    {
        loginPanel?.SetActive(true);
        registerPanel?.SetActive(false);
        ClearStatus();
        ClearLoginFields();
    }

    private void ShowRegisterPanel()
    {
        loginPanel?.SetActive(false);
        registerPanel?.SetActive(true);
        ClearStatus();
        ClearRegisterFields();
    }

    // ── UI Utility ───────────────────────────────────────────────────────────

    private void SetStatus(string message, Color colour)
    {
        if (statusText == null) return;
        statusText.text  = message;
        statusText.color = colour;
    }

    private void ClearStatus()
    {
        if (statusText == null) return;
        statusText.text = string.Empty;
    }

    private void SetLoading(bool isLoading)
    {
        // Disable interactive buttons while a request is in flight
        SetInteractable(!isLoading);

        if (loadingOverlay == null) return;
        loadingOverlay.alpha          = isLoading ? 0.6f : 0f;
        loadingOverlay.blocksRaycasts = isLoading;
    }

    private void SetInteractable(bool interactable)
    {
        if (loginButton)       loginButton.interactable       = interactable;
        if (registerButton)    registerButton.interactable    = interactable;  // LoginPanel → navigate
        if (signupButton)      signupButton.interactable      = interactable;  // RegisterPanel → submit
        if (backToLoginButton) backToLoginButton.interactable = interactable;
    }

    private void ClearLoginFields()
    {
        if (emailInput)    emailInput.text    = string.Empty;
        if (passwordInput) passwordInput.text = string.Empty;
    }

    private void ClearRegisterFields()
    {
        if (registerEmailInput)    registerEmailInput.text    = string.Empty;
        if (registerPasswordInput) registerPasswordInput.text = string.Empty;
        if (confirmPasswordInput)  confirmPasswordInput.text  = string.Empty;
    }

    // ── Validation ───────────────────────────────────────────────────────────

    /// <summary>Returns true if the string is a structurally valid email address.</summary>
    private static bool ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    // ── Error Parsing ────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a Firebase AggregateException into a human-readable message.
    /// </summary>
    private static string GetFirebaseErrorMessage(AggregateException exception)
    {
        if (exception == null) return "An unknown error occurred.";

        Firebase.FirebaseException fbEx =
            exception.GetBaseException() as Firebase.FirebaseException;

        if (fbEx != null)
        {
            AuthError code = (AuthError)fbEx.ErrorCode;

            switch (code)
            {
                case AuthError.InvalidEmail:
                    return "The email address is badly formatted.";
                case AuthError.WrongPassword:
                    return "Incorrect password. Please try again.";
                case AuthError.UserNotFound:
                    return "No account found with this email.";
                case AuthError.EmailAlreadyInUse:
                    return "An account with this email already exists.";
                case AuthError.WeakPassword:
                    return "Password is too weak (minimum 6 characters).";
                case AuthError.NetworkRequestFailed:
                    return "Network error. Check your connection and retry.";
                case AuthError.TooManyRequests:
                    return "Too many attempts. Please wait before trying again.";
                case AuthError.UserDisabled:
                    return "This account has been disabled.";
                case AuthError.OperationNotAllowed:
                    return "Email/password sign-in is not enabled in Firebase.";
                default:
                    return $"Authentication error ({code}). Please try again.";
            }
        }

        return "An unexpected error occurred. Please try again.";
    }
}
