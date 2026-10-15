import { test } from "node:test"
import assert from "node:assert/strict"
import { maskText, normalizeDocument } from "../normalize.ts"

const seedTime = Date.parse("2026-03-02T16:00:00Z")
const cachedBoostForms = [
  '<form action="/messages/5/boosts" method="post">TOKEN<button>Boost</button></form>',
  '<form action="/messages/5/boosts/6" class="button_to" method="post"><input name="_method" type="hidden" value="delete"><button data-action="boost-delete#perform">Delete</button>TOKEN</form>',
]

test("decodes signed ids behind a URL path", () => {
  const url = "/rails/active_storage/blobs/redirect/eyJfcmFpbHMiOnsiZGF0YSI6NSwicHVyIjoiYmxvYl9pZCJ9fQ==--4ceb3a7460a929db324ca5fd0dffee9c8527bfad/moon.jpg"
  assert.equal(maskText(url, { seedTime }), "/rails/active_storage/blobs/redirect/«signed_id:blob_id:5»/moon.jpg")
})

test("decodes avatar tokens", () => {
  const token = "eyJfcmFpbHMiOnsiZGF0YSI6MTI3MzI2MTQxLCJwdXIiOiJ1c2VyL2F2YXRhciJ9fQ--0ac8233a4786ee6c413408416bb81e7107534db5702d351533dc2ecd3876e97f"
  assert.equal(maskText(`/users/${token}/avatar`, { seedTime }), "/users/«signed_id:user/avatar:127326141»/avatar")
})

test("decodes turbo signed stream names", () => {
  const name = "IloybGtPaTh2WTJGdGNHWnBjbVV2VW05dmJYTTZPa05zYjNObFpDODJOVFEyTXpJNE56WTptZXNzYWdlcyI=--02b7b2332eb7e12ec765aa332a46ce0a08a11c822d2ebd26710899f01d4392bd"
  assert.equal(maskText(name, { seedTime }), "«stream:Rooms::Closed#654632876:messages»")
})

test("a token for another record still differs", () => {
  const a = maskText("eyJfcmFpbHMiOnsiZGF0YSI6NSwicHVyIjoiYmxvYl9pZCJ9fQ==--4ceb3a7460a929db324ca5fd0dffee9c8527bfad", { seedTime })
  const b = maskText("eyJfcmFpbHMiOnsiZGF0YSI6NiwicHVyIjoiYmxvYl9pZCJ9fQ==--4ceb3a7460a929db324ca5fd0dffee9c8527bfad", { seedTime })
  assert.notEqual(a, b)
})

test("normalizes attributes, whitespace, csrf and timestamps", () => {
  const html = `<!DOCTYPE html><html><head><meta name="csrf-token" content="abc"></head><body>
    <div  id="x"   class="b a" data-message-timestamp="1772463600000">  hello
      world </div><time datetime="2026-03-02T15:00:00Z"></time><pre>  keep\n me</pre></body></html>`
  assert.equal(normalizeDocument(html, { seedTime }), [
    "<!DOCTYPE html>",
    "<html>",
    "  <head>",
    `    <meta content="«csrf»" name="csrf-token">`,
    "  </head>",
    "  <body>",
    `    <div class="b a" data-message-timestamp="«epochms-3600s»" id="x">`,
    "      hello world",
    "    </div>",
    `    <time datetime="«t-3600s»">`,
    "    </time>",
    "    <pre>",
    `      "  keep\\n me"`,
    "    </pre>",
    "  </body>",
    "</html>",
    "",
  ].join("\n"))
})

test("decodes consecutive tokens in one URL", () => {
  const url = "/rails/active_storage/representations/redirect/eyJfcmFpbHMiOnsiZGF0YSI6NSwicHVyIjoiYmxvYl9pZCJ9fQ==--4ceb3a7460a929db324ca5fd0dffee9c8527bfad/eyJfcmFpbHMiOnsiZGF0YSI6eyJmb3JtYXQiOiJqcGciLCJyZXNpemVfdG9fbGltaXQiOlsxMjAwLDgwMF19LCJwdXIiOiJ2YXJpYXRpb24ifX0=--28426ca1e33b0fea71b8b10b7f52a844de5886cf/moon.jpg"
  assert.equal(maskText(url, { seedTime }), `/rails/active_storage/representations/redirect/«signed_id:blob_id:5»/«signed_id:variation:{"format":"jpg","resize_to_limit":[1200,800]}»/moon.jpg`)
})

test("verified opaque tokens keep record and purpose identity; unknown tokens remain visible", () => {
  const token = "CfDJ8" + "A".repeat(60)
  const other = "CfDJ8" + "B".repeat(60)
  const unknown = "CfDJ8" + "C".repeat(60)
  const options = { protectedTokens: new Map([[token, "«signed_id:user/avatar:5»"], [other, "«signed_id:user/avatar:6»"]]) }
  assert.equal(maskText(`/users/${token}/avatar`, options), "/users/«signed_id:user/avatar:5»/avatar")
  assert.notEqual(maskText(token, options), maskText(other, options))
  assert.equal(maskText(unknown, options), unknown)
  assert.notEqual(maskText(token, options), "«signed_id:user/transfer:5»")
})

test("native cached boost forms omit only their hidden CSRF input", () => {
  const input = '<input type="hidden" name="authenticity_token" value="random">'
  const native = { nativeComparison: true }
  for (const form of cachedBoostForms) {
    const withToken = form.replace("TOKEN", input)
    const without = form.replace("TOKEN", "")
    assert.equal(normalizeDocument(withToken, native), normalizeDocument(without, native))
    assert.notEqual(normalizeDocument(form.replace("TOKEN", input + input), native), normalizeDocument(without, native), "duplicate token fields remain visible")
    assert.notEqual(normalizeDocument(withToken), normalizeDocument(without), "generic comparison keeps the token")
    for (const [changedForm, changedToken] of [
      [form.replace("/boosts", "/boost"), input], [form.replace('method="post"', 'method="get"'), input],
      [form, input.replace('type="hidden"', 'type="text"')],
      [form, input.replace('name="authenticity_token"', 'name="other_token"')],
      [form, input.replace('<input ', '<input style="display:none" ')],
      [form, input.replace('<input ', '<input style="" data-other="" ')],
    ]) {
      assert.notEqual(normalizeDocument(changedForm.replace("TOKEN", changedToken), native), normalizeDocument(changedForm.replace("TOKEN", ""), native))
    }
  }
  const deleted = cachedBoostForms[1]
  for (const changed of [
    deleted.replace('value="delete"', 'value="patch"'), deleted.replace("boost-delete#perform", "other#perform"), deleted.replace("button_to", "other"),
    deleted.replace('name="_method"', 'name="_method" style="display:none"'),
    deleted.replace('name="_method"', 'name="_method" style="" data-other=""'),
  ]) {
    assert.notEqual(normalizeDocument(changed.replace("TOKEN", input), native), normalizeDocument(changed.replace("TOKEN", ""), native))
  }
  assert.notEqual(normalizeDocument(`<form action="/session" method="post">${input}</form>`, native), normalizeDocument('<form action="/session" method="post"></form>', native))
})

test("native cached boost CSRF omission includes only the real empty-style variant", () => {
  const input = '<input name="authenticity_token" style="" type="hidden" value="random">'
  for (const form of cachedBoostForms) {
    const liveForm = form.replace('name="_method"', 'name="_method" style=""')
    const expected = liveForm.replace("TOKEN", input)
    const actual = liveForm.replace("TOKEN", "")
    assert.equal(normalizeDocument(expected, { nativeComparison: true }), normalizeDocument(actual, { nativeComparison: true }))
    assert.notEqual(normalizeDocument(expected), normalizeDocument(actual), "generic comparison retains the empty-style token")
    assert.notEqual(normalizeDocument(liveForm.replace("TOKEN", input + input), { nativeComparison: true }), normalizeDocument(actual, { nativeComparison: true }), "duplicate empty-style token fields remain visible")
  }
})

test("native account forms alias only the approved synthetic Rails action", () => {
  const rails = '<form action="/account.873240054" data-controller="form" method="post"><input name="_method" type="hidden" value="patch"><input id="account_name" name="account[name]" value="Campfire"></form>'
  const native = rails.replace("/account.873240054", "/account")
  const profile = { nativeComparison: true }
  assert.equal(normalizeDocument(rails, profile), normalizeDocument(native, profile))
  assert.notEqual(normalizeDocument(rails), normalizeDocument(native))
  for (const changed of [
    rails.replace("873240054", "873240055"), rails.replace("/account.873240054", "/account.873240054/other"),
    rails.replace('method="post"', 'method="get"'), rails.replace('data-controller="form"', 'data-controller="other"'),
    rails.replace('value="patch"', 'value="delete"'), rails.replace('value="Campfire"', 'value="Wrong account"'),
  ]) assert.notEqual(normalizeDocument(changed, profile), normalizeDocument(native, profile))
})
