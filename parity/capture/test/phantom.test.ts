import { test } from "node:test"
import assert from "node:assert/strict"
import { runInNewContext } from "node:vm"
import type { Page, Response } from "playwright"
import { NetworkLog } from "../network.ts"
import { PageTracker, settle } from "../readiness.ts"
import { WireReceipts } from "../wire_receipts.ts"

function fixture(overrides: Record<string,unknown> = {}) {
  const callbacks = new Map<string,Function[]>()
  const emit = (event:string, value:unknown) => callbacks.get(event)?.forEach(callback=>callback(value))
  const video={src:"http://localhost:3999/video",preload:"none",paused:true,error:null,readyState:0,networkState:1}
  const page={on:(event:string,callback:Function)=>{callbacks.set(event,[...(callbacks.get(event)??[]),callback])},url:()=>"http://localhost:3999/room",evaluate:async(fn:Function,arg:unknown)=>runInNewContext(`(${fn.toString()})(arg)`,{arg,document:{querySelectorAll:()=>overrides.videos??[video]}}),context:()=>({browser:()=>({browserType:()=>({name:()=>overrides.engine??"webkit"})}),request:{get:async()=>assert.fail("unexpected body refetch")}})} as unknown as Page
  const response=(status:number,kind="other",body="",headers:Record<string,string>={})=>({
    status:()=>status,url:()=>"http://localhost:3999/video",headers:()=>headers,allHeaders:async()=>headers,headersArray:async()=>[],body:async()=>Buffer.from(body),
    request:()=>({method:()=>overrides.method??"GET",resourceType:()=>overrides.kind??kind,headers:()=>overrides.requestHeaders??{},redirectedFrom:()=>overrides.redirected??null,failure:()=>overrides.failure??null,timing:()=>({requestStart:overrides.requestStart??-1}),url:()=>"http://localhost:3999/video",isNavigationRequest:()=>false}),
  }) as unknown as Response
  return {page,emit,response}
}

test("only the unloaded WebKit video placeholder is excluded; real redirect and final image bytes stay recorded",async()=>{
  const f=fixture(); const log=new NetworkLog(f.page,"http://localhost:3999",{})
  f.emit("response",f.response(302,"image","",{"content-type":"text/html",location:"/poster"}))
  f.emit("response",f.response(0))
  const poster=f.response(200,"image","real image bytes",{"content-type":"image/webp"})
  f.emit("response",{...poster,url:()=>"http://localhost:3999/poster",request:()=>({...poster.request(),url:()=>"http://localhost:3999/poster"})})
  const result=await log.text()
  assert.match(result,/GET \/video → 302/)
  assert.match(result,/GET \/poster → 200/)
  assert.match(result,/body: sha256:/)
  assert.doesNotMatch(result,/→ 0/)
})

test("unmatched zero receipts and evidence of real media work or failures cannot use the placeholder exclusion",async()=>{
  const video={src:"http://localhost:3999/video",preload:"none",paused:true,error:null,readyState:0,networkState:1}
  const wrongVideos=[[],[{...video,src:"http://localhost:3999/wrong"}],[{...video,preload:"auto"}],[{...video,paused:false}],[{...video,error:{code:3}}],[{...video,readyState:1}],[{...video,networkState:3}]].map(videos=>({videos}))
  for(const overrides of [{engine:"chromium"},{method:"POST"},{kind:"media"},{requestHeaders:{accept:"video/*"}},{failure:{errorText:"ERR_FAILED"}},{redirected:{}},{requestStart:0},...wrongVideos]) {
    const f=fixture(overrides); const log=new NetworkLog(f.page,"http://localhost:3999",{})
    f.emit("response",f.response(0))
    await assert.rejects(log.text(),(error:any)=>String(error.cause).includes("Unmatched non-HTTP"))
  }
  const f=fixture(); const log=new NetworkLog(f.page,"http://localhost:3999",{})
  f.emit("response",f.response(0,"other","",{"content-type":"video/mp4"}))
  await assert.rejects(log.text())
})

test("a synthetic receipt cannot overwrite a real same-URL HTTP failure",async()=>{
  const f=fixture();const log=new NetworkLog(f.page,"http://localhost:3999",{})
  f.emit("response",f.response(404,"other","media missing",{"content-type":"text/plain"}))
  f.emit("response",f.response(0))
  const result=await log.text()
  assert.match(result,/GET \/video → 404/)
  assert.match(result,/body: sha256:/)
})

test("PageTracker still fails readiness for a genuine broken media request",async()=>{
  const f=fixture(); const tracker=new PageTracker(f.page)
  f.emit("requestfailed",{resourceType:()=>"media",failure:()=>({errorText:"ERR_FAILED"}),url:()=>"http://localhost:3999/video"})
  await assert.rejects(settle(tracker,100,undefined),/network failure: ERR_FAILED/)
})

test("WebKit native redirects verify omitted protocol headers against actual matching wire evidence",async()=>{
 const required={"x-content-type-options":"nosniff","x-frame-options":"SAMEORIGIN","referrer-policy":"strict-origin-when-cross-origin"}
 for(const wire of [required,{...required,"x-content-type-options":""},{...required,"x-frame-options":""},{...required,"referrer-policy":""}]) {
  const f=fixture(); const receipts=new WireReceipts();const log=new NetworkLog(f.page,"http://localhost:3999",{nativeTarget:true},receipts.verifier())
  receipts.record({method:"GET",url:"http://localhost:3999/video",status:302,location:"/poster"},wire)
  f.emit("response",f.response(302,"image","",{location:"/poster"}))
  if(wire===required) assert.match(await log.text(),/→ 302/)
  else await assert.rejects(log.text(),(error:any)=>String(error.cause).includes("must be"))
 }
 const f=fixture();const log=new NetworkLog(f.page,"http://localhost:3999",{nativeTarget:true},new WireReceipts().verifier())
 f.emit("response",f.response(302,"image","",{location:"/poster"}))
 await assert.rejects(log.text(),(error:any)=>String(error.cause).includes("found 0"))
})

test("a later same-URL success cannot overwrite a native security failure",async()=>{
 const f=fixture({engine:"chromium"});const log=new NetworkLog(f.page,"http://localhost:3999",{nativeTarget:true})
 f.emit("response",f.response(200,"image","bad",{"content-type":"image/png"}))
 f.emit("response",f.response(200,"image","good",{"content-type":"image/png","x-content-type-options":"nosniff","x-frame-options":"SAMEORIGIN","referrer-policy":"strict-origin-when-cross-origin"}))
 await assert.rejects(log.text(),(error:any)=>String(error.cause).includes("must be"))
})

test("a ready-capture snapshot does not mix unchecked later responses into its latest map",async()=>{
 const f=fixture({engine:"chromium"});const log=new NetworkLog(f.page,"http://localhost:3999",{nativeTarget:true})
 const required={"content-type":"image/png","x-content-type-options":"nosniff","x-frame-options":"SAMEORIGIN","referrer-policy":"strict-origin-when-cross-origin"}
 let release!:()=>void;const gate=new Promise<void>((resolve)=>{release=resolve})
 const first=f.response(200,"image","first",required)
 f.emit("response",{...first,body:async()=>{await gate;return Buffer.from("first")}})
 const snapshot=log.text()
 f.emit("response",f.response(200,"image","bad",{"content-type":"image/png"}))
 f.emit("response",f.response(200,"image","good",required))
 release()
 const text=await snapshot
 const expectedFixture=fixture({engine:"chromium"});const expectedLog=new NetworkLog(expectedFixture.page,"http://localhost:3999",{nativeTarget:true})
 expectedFixture.emit("response",expectedFixture.response(200,"image","first",required))
 assert.equal(text,await expectedLog.text())
 await assert.rejects(log.text(),(error:any)=>String(error.cause).includes("must be"))
})
