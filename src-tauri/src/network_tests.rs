use crate::api::{self, Provider};
use serde_json::{Value, json};
use std::{
    io::{Read, Write},
    net::TcpListener,
    sync::{Arc, Mutex},
    thread,
    time::{Duration, Instant},
};
use tokio_util::sync::CancellationToken;

struct Response {
    status: u16,
    headers: Vec<(&'static str, String)>,
    body: String,
    delay: Duration,
}
fn response(status: u16, body: Value) -> Response {
    Response {
        status,
        headers: vec![],
        body: body.to_string(),
        delay: Duration::ZERO,
    }
}
fn translated(text: &str) -> Value {
    json!({"choices":[{"finish_reason":"stop","message":{"content":text}}]})
}
struct Fake {
    url: String,
    requests: Arc<Mutex<Vec<(String, Value)>>>,
    worker: Option<thread::JoinHandle<()>>,
}
impl Drop for Fake {
    fn drop(&mut self) {
        if let Some(worker) = self.worker.take() {
            worker.join().unwrap()
        }
    }
}
fn serve(responses: Vec<Response>) -> Fake {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    listener.set_nonblocking(true).unwrap();
    let url = format!("http://{}/prefix/", listener.local_addr().unwrap());
    let requests = Arc::new(Mutex::new(vec![]));
    let received = requests.clone();
    let worker = thread::spawn(move || {
        for response in responses {
            let start = Instant::now();
            let mut socket = loop {
                match listener.accept() {
                    Ok((s, _)) => break s,
                    Err(e)
                        if e.kind() == std::io::ErrorKind::WouldBlock
                            && start.elapsed() < Duration::from_secs(3) =>
                    {
                        thread::sleep(Duration::from_millis(3))
                    }
                    _ => return,
                }
            };
            // Winsock accepts inherit the listener's nonblocking mode.
            socket.set_nonblocking(false).unwrap();
            let _ = socket.set_read_timeout(Some(Duration::from_secs(2)));
            let mut bytes = vec![];
            let mut buffer = [0; 4096];
            loop {
                let Ok(count) = socket.read(&mut buffer) else {
                    return;
                };
                if count == 0 {
                    return;
                }
                bytes.extend_from_slice(&buffer[..count]);
                if bytes.len() > 65536 {
                    return;
                }
                if let Some(end) = bytes.windows(4).position(|v| v == b"\r\n\r\n") {
                    let headers = String::from_utf8_lossy(&bytes[..end]).to_string();
                    let length = headers
                        .lines()
                        .find_map(|l| {
                            l.to_lowercase()
                                .strip_prefix("content-length:")
                                .and_then(|v| v.trim().parse::<usize>().ok())
                        })
                        .unwrap_or(0);
                    if bytes.len() >= end + 4 + length {
                        let json = serde_json::from_slice(&bytes[end + 4..end + 4 + length])
                            .unwrap_or(Value::Null);
                        received.lock().unwrap().push((headers, json));
                        break;
                    }
                }
            }
            thread::sleep(response.delay);
            let extras = response
                .headers
                .into_iter()
                .map(|(k, v)| format!("{k}: {v}\r\n"))
                .collect::<String>();
            let _ = write!(
                socket,
                "HTTP/1.1 {} Fixture\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n{}\r\n{}",
                response.status,
                response.body.len(),
                extras,
                response.body
            );
        }
    });
    Fake {
        url,
        requests,
        worker: Some(worker),
    }
}

#[tokio::test]
async fn minimal_protocol_and_endpoint() {
    let fake = serve(vec![response(200, translated("Hello"))]);
    let p = Provider::new(
        api::client(),
        &fake.url,
        "namespace/model:free",
        "fake-fixture-key",
    )
    .unwrap();
    assert_eq!(
        p.translate("你好", &CancellationToken::new())
            .await
            .unwrap(),
        "Hello"
    );
    let requests = fake.requests.lock().unwrap();
    let (headers, body) = &requests[0];
    assert!(headers.starts_with("POST /prefix/chat/completions "));
    assert!(
        headers
            .to_lowercase()
            .contains("authorization: bearer fake-fixture-key")
    );
    assert_eq!(body.as_object().unwrap().len(), 3);
    assert_eq!(body["model"], "namespace/model:free");
    assert_eq!(body["stream"], false);
    assert!(
        body["messages"][0]["content"]
            .as_str()
            .unwrap()
            .contains("into English")
    );
    assert_eq!(body["messages"][1]["content"], "你好")
}
#[tokio::test]
async fn language_pair_reaches_request_and_preserves_multiline_contract() {
    use crate::language::{LANGUAGES, LanguagePair, TargetLanguage};
    for language in LANGUAGES {
        let target: TargetLanguage = serde_json::from_value(json!(language.code)).unwrap();
        assert_eq!(target.definition().code, language.code);
        let fake = serve(vec![response(
            200,
            translated(r#"{"lines":["one","","two"]}"#),
        )]);
        let result = Provider::new(api::client(), &fake.url, "model", "")
            .unwrap()
            .with_languages(LanguagePair::new(target, target.other_default()).unwrap())
            .translate("Hello\r\n\r\nworld", &CancellationToken::new())
            .await
            .unwrap();
        assert_eq!(result, "one\r\n\r\ntwo");
        let requests = fake.requests.lock().unwrap();
        let body = &requests[0].1;
        assert_eq!(body.as_object().unwrap().len(), 3);
        let prompt = body["messages"][0]["content"].as_str().unwrap();
        assert!(prompt.contains(&format!("primary language is {}.", language.prompt)));
        assert!(prompt.contains(&format!(
            "other language is {}.",
            target.other_default().definition().prompt
        )));
        assert!(prompt.contains("otherwise, including any third language"));
        assert_eq!(requests.len(), 1);
        assert!(prompt.contains("exactly 3 strings"));
        assert_eq!(body["messages"][1]["content"], "Hello\r\n\r\nworld");
    }
}
#[tokio::test]
async fn japanese_pair_sends_both_directions_and_third_language_in_one_request_each() {
    use crate::language::{LanguagePair, TargetLanguage};
    let sources = ["こんにちは、世界。", "Hello, world.", "你好，世界。"];
    let outputs = ["Hello, world.", "こんにちは、世界。", "こんにちは、世界。"];
    let fake = serve(
        outputs
            .iter()
            .map(|text| response(200, translated(text)))
            .collect(),
    );
    let pair = LanguagePair::new(TargetLanguage::Japanese, TargetLanguage::English).unwrap();
    let provider = Provider::new(api::client(), &fake.url, "model", "")
        .unwrap()
        .with_languages(pair);
    for (source, output) in sources.iter().zip(outputs) {
        assert_eq!(
            provider
                .translate(source, &CancellationToken::new())
                .await
                .unwrap(),
            output
        );
    }
    let requests = fake.requests.lock().unwrap();
    assert_eq!(requests.len(), sources.len());
    for ((_, body), source) in requests.iter().zip(sources) {
        assert_eq!(body["messages"][1]["content"], source);
        let prompt = body["messages"][0]["content"].as_str().unwrap();
        assert!(prompt.contains("If the source is mainly Japanese, translate all of it into English; otherwise, including any third language, translate all of it into Japanese."));
        assert!(prompt.contains("distinguish Japanese from Chinese"));
    }
}

#[tokio::test]
async fn invalid_language_pair_is_rejected_before_network() {
    use crate::language::{LanguagePair, TargetLanguage};
    let fake = serve(vec![]);
    let provider = Provider::new(api::client(), &fake.url, "model", "")
        .unwrap()
        .with_languages(LanguagePair {
            primary: TargetLanguage::English,
            secondary: TargetLanguage::English,
        });
    assert!(
        provider
            .translate("Hello", &CancellationToken::new())
            .await
            .is_err()
    );
    assert!(fake.requests.lock().unwrap().is_empty());
}

#[tokio::test]
async fn anonymous_endpoint_has_no_authorization() {
    let fake = serve(vec![response(200, translated("Hello"))]);
    Provider::new(api::client(), &fake.url, "model", "")
        .unwrap()
        .translate("原文", &CancellationToken::new())
        .await
        .unwrap();
    assert!(
        !fake.requests.lock().unwrap()[0]
            .0
            .to_lowercase()
            .contains("authorization:")
    )
}
#[tokio::test]
async fn authentication_error_is_redacted() {
    let fake = serve(vec![response(
        401,
        json!({"error":"fake-secret and private text"}),
    )]);
    let e = Provider::new(api::client(), &fake.url, "model", "")
        .unwrap()
        .translate("原文", &CancellationToken::new())
        .await
        .unwrap_err();
    assert!(e.message.contains("认证"));
    assert!(!e.message.contains("private"));
    assert!(!e.message.contains("fake-secret"))
}
#[tokio::test]
async fn rate_limit_wait_is_preserved() {
    let mut r = response(429, json!({}));
    r.headers.push(("Retry-After", "7".into()));
    let fake = serve(vec![r]);
    let e = Provider::new(api::client(), &fake.url, "model", "")
        .unwrap()
        .translate("原文", &CancellationToken::new())
        .await
        .unwrap_err();
    assert_eq!(e.retry_after, Some(Duration::from_secs(7)))
}
#[tokio::test]
async fn redirect_never_receives_key() {
    let mut r = response(302, json!({}));
    r.headers
        .push(("Location", "https://example.invalid/steal".into()));
    let fake = serve(vec![r]);
    assert!(
        Provider::new(api::client(), &fake.url, "model", "fake-key")
            .unwrap()
            .translate("原文", &CancellationToken::new())
            .await
            .is_err()
    );
    assert_eq!(fake.requests.lock().unwrap().len(), 1)
}
#[tokio::test]
async fn oversized_response_is_rejected() {
    let mut r = response(200, translated("Hello"));
    r.body = "x".repeat(65537);
    let fake = serve(vec![r]);
    let e = Provider::new(api::client(), &fake.url, "model", "")
        .unwrap()
        .translate("原文", &CancellationToken::new())
        .await
        .unwrap_err();
    assert!(e.message.contains("过大"))
}
#[tokio::test]
async fn cancellation_discards_late_response() {
    let mut r = response(200, translated("late"));
    r.delay = Duration::from_millis(150);
    let fake = serve(vec![r]);
    let token = CancellationToken::new();
    let cancel = token.clone();
    tokio::spawn(async move {
        tokio::time::sleep(Duration::from_millis(30)).await;
        cancel.cancel()
    });
    let e = Provider::new(api::client(), &fake.url, "model", "")
        .unwrap()
        .translate("原文", &token)
        .await
        .unwrap_err();
    assert!(e.message.contains("取消"))
}
#[tokio::test]
async fn model_pagination_and_deduplication() {
    let fake = serve(vec![
        response(
            200,
            json!({"data":[{"id":"z"},{"id":"A"}],"has_more":true,"last_id":"cursor"}),
        ),
        response(200, json!({"data":[{"id":"A"},{"id":"model:free"}]})),
    ]);
    let ids = Provider::models(&api::client(), &fake.url, "", &CancellationToken::new())
        .await
        .unwrap();
    assert_eq!(ids, vec!["A", "model:free", "z"]);
    assert!(
        fake.requests.lock().unwrap()[1]
            .0
            .starts_with("GET /prefix/models?after=cursor ")
    )
}
#[tokio::test]
async fn model_query_has_manual_fallback() {
    let fake = serve(vec![response(404, json!({}))]);
    let error = Provider::models(&api::client(), &fake.url, "", &CancellationToken::new())
        .await
        .unwrap_err();
    assert!(error.contains("手动填写"))
}
#[tokio::test]
async fn repeated_model_cursor_is_rejected() {
    let body = json!({"data":[{"id":"model"}],"has_more":true,"last_id":"same"});
    let fake = serve(vec![response(200, body.clone()), response(200, body)]);
    assert!(
        Provider::models(&api::client(), &fake.url, "", &CancellationToken::new())
            .await
            .is_err()
    )
}
