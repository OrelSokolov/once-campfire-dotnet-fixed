import { test } from "node:test"
import assert from "node:assert/strict"
import { WireReceipts } from "../wire_receipts.ts"

const request={method:"GET",url:"http://localhost:3999/poster",status:302,location:"/image"}
const headers={"x-content-type-options":"nosniff","x-frame-options":"SAMEORIGIN","referrer-policy":"strict-origin-when-cross-origin"}

test("wire evidence is fresh, target-scoped, exact and consumed once",()=>{
 const target=new WireReceipts(); target.record(request,headers)
 const verify=target.verifier()
 assert.throws(()=>verify(request),/found 0/)
 const other=new WireReceipts(); other.record(request,headers)
 assert.throws(()=>verify(request),/found 0/)
 target.record(request,headers)
 for(const mismatch of [{method:"POST"},{url:"http://localhost:3999/wrong"},{status:301},{location:"/wrong"}]) assert.throws(()=>verify({...request,...mismatch}),/found 0/)
 assert.deepEqual(verify(request),headers)
 assert.throws(()=>verify(request),/found 0/)
})

test("ambiguous wire evidence fails rather than choosing a favorable response",()=>{
 const target=new WireReceipts(); const verify=target.verifier()
 target.record(request,headers);target.record(request,{...headers,"x-frame-options":""})
 assert.throws(()=>verify(request),/found 2/)
})

test("wire evidence preserves missing actual security headers and copies immutable snapshots",()=>{
 const target=new WireReceipts();const verify=target.verifier()
 const mutable={"referrer-policy":"strict-origin-when-cross-origin"}
 target.record(request,mutable);mutable["referrer-policy"]="changed"
 const actual=verify(request)
 assert.equal(actual["referrer-policy"],"strict-origin-when-cross-origin")
 assert.equal(actual["x-content-type-options"],undefined)
 assert.equal(actual["x-frame-options"],undefined)
 assert.equal(Object.isFrozen(actual),true)
})
