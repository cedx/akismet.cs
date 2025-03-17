namespace Belin.Akismet;

/// <summary>
/// Submits comments to the Akismet service.
/// </summary>
/// <param name="apiKey">The Akismet API key.</param>
/// <param name="blog">The front page or home URL of the instance making requests.</param>
/// <param name="baseUrl">The base URL of the remote API endpoint.</param>
public class Client(string apiKey, Blog blog, string baseUrl = "https://rest.akismet.com") {

	/// <summary>
	/// The response returned by the <c>submit-ham</c> and <c>submit-spam</c> endpoints when the outcome is a success.
	/// </summary>
	private const string Success = "Thanks for making the web a better place.";

	/// <summary>
	/// The package version.
	/// </summary>
	private const string Version = "1.0.0";

	/// <summary>
	/// The Akismet API key.
	/// </summary>
	public string ApiKey { get; } = apiKey;

	/// <summary>
	/// The base URL of the remote API endpoint.
	/// </summary>
	public Uri BaseUrl { get; } = new Uri(baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/");

	/// <summary>
	/// The front page or home URL of the instance making requests.
	/// </summary>
	public Blog Blog { get; } = blog;

	/// <summary>
	/// Value indicating whether the client operates in test mode.
	/// </summary>
	public bool IsTest { get; init; } = false;

	/// <summary>
	/// The user agent string to use when making requests.
	/// </summary>
	public string UserAgent { get; init; } = $".NET/{Environment.Version.ToString(3)} | Akismet/{Version}";

	/// <summary>
	/// Checks the specified comment against the service database, and returns a value indicating whether it is spam.
	/// </summary>
	/// <param name="comment">The comment to be submitted.</param>
	/// <param name="cancellationToken">The token to cancel the operation.</param>
	/// <returns>A value indicating whether the specified comment is spam.</returns>
	/// <exception cref="HttpRequestException">The remote server returned an invalid response.</exception>
	public async Task<CheckResult> CheckComment(Comment comment, CancellationToken cancellationToken = default) {
		using var response = await Fetch("1.1/comment-check", comment.ToDictionary(), cancellationToken);
		if (await response.Content.ReadAsStringAsync(cancellationToken) == "false") return CheckResult.Ham;
		if (!response.Headers.TryGetValues("X-akismet-pro-tip", out var proTips)) return CheckResult.Spam;
		return proTips.First() == "discard" ? CheckResult.PervasiveSpam : CheckResult.Spam;
	}

	/// <summary>
	/// Submits the specified comment that was incorrectly marked as spam but should not have been.
	/// </summary>
	/// <param name="comment">The comment to be submitted.</param>
	/// <param name="cancellationToken">The token to cancel the operation.</param>
	/// <returns>Completes once the comment has been submitted.</returns>
	/// <exception cref="HttpRequestException">The remote server returned an invalid response.</exception>
	public async Task SubmitHam(Comment comment, CancellationToken cancellationToken = default) {
		using var response = await Fetch("1.1/submit-ham", comment.ToDictionary(), cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (body != Success) throw new HttpRequestException("Invalid server response.");
	}

	/// <summary>
	/// Submits the specified comment that was not marked as spam but should have been.
	/// </summary>
	/// <param name="comment">The comment to be submitted.</param>
	/// <param name="cancellationToken">The token to cancel the operation.</param>
	/// <returns>Completes once the comment has been submitted.</returns>
	/// <exception cref="HttpRequestException">The remote server returned an invalid response.</exception>
	public async Task SubmitSpam(Comment comment, CancellationToken cancellationToken = default) {
		using var response = await Fetch("1.1/submit-spam", comment.ToDictionary(), cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (body != Success) throw new HttpRequestException("Invalid server response.");
	}

	/// <summary>
	/// Checks the API key against the service database, and returns a value indicating whether it is valid.
	/// </summary>
	/// <param name="cancellationToken">The token to cancel the operation.</param>
	/// <returns><see langword="true"/> if the specified API key is valid, otherwise <see langword="false"/>.</returns>
	public async Task<bool> VerifyKey(CancellationToken cancellationToken = default) {
		try {
			using var response = await Fetch("1.1/verify-key", cancellationToken: cancellationToken);
			return await response.Content.ReadAsStringAsync(cancellationToken) == "valid";
		}
		catch {
			return false;
		}
	}

	/// <summary>
	/// Queries the service by posting the specified fields to a given end point, and returns the response.
	/// </summary>
	/// <param name="endpoint">The relative URL of the end point to query.</param>
	/// <param name="fields">The fields describing the query body.</param>
	/// <param name="cancellationToken">The token to cancel the operation.</param>
	/// <returns>The server response.</returns>
	/// <exception cref="HttpRequestException">An error occurred while querying the end point.</exception>
	private async Task<HttpResponseMessage> Fetch(string endpoint, IDictionary<string, string>? fields = null, CancellationToken cancellationToken = default) {
		var postFields = Blog.ToDictionary();
		postFields.Add("api_key", ApiKey);
		if (IsTest) postFields.Add("is_test", "1");
		if (fields is not null) foreach (var item in fields) postFields.Add(item.Key, item.Value);

		using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUrl, endpoint)) { Content = new FormUrlEncodedContent(postFields) };
		request.Headers.Add("User-Agent", UserAgent);

		using var httpClient = new HttpClient();
		var response = await httpClient.SendAsync(request, cancellationToken);
		response.EnsureSuccessStatusCode();
		if (response.Headers.TryGetValues("X-akismet-alert-msg", out var alertMessages)) throw new HttpRequestException(alertMessages.First());
		if (response.Headers.TryGetValues("X-akismet-debug-help", out var debugHelps)) throw new HttpRequestException(debugHelps.First());
		return response;
	}
}
