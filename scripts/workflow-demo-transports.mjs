import http from 'node:http';
import net from 'node:net';
import { randomUUID } from 'node:crypto';

// Loopback-only test services: no messages are sent to real recipients.
const operations = new Map();
let fail = true;
http.createServer(async (req,res) => {
  let body=''; for await(const part of req) {body+=part;if(body.length>262144){res.writeHead(413).end();return;}}
  if(req.url==='/health'){res.end('ok');return;}
  if(req.url==='/recover' && req.method==='POST'){fail=false;res.end('ok');return;}
  if(req.url==='/fail' && fail){res.writeHead(503).end('Demo provider unavailable; POST /recover then retry.');return;}
  const key=req.headers['idempotency-key'] || randomUUID();
  if(!operations.has(key))operations.set(key,{id:randomUUID(),accepted:true});
  if(req.url==='/soap'){
    const namespace=body.includes('www.w3.org/2003/05/soap-envelope')?'http://www.w3.org/2003/05/soap-envelope':'http://schemas.xmlsoap.org/soap/envelope/';
    res.setHeader('Content-Type','text/xml');res.end(`<soap:Envelope xmlns:soap="${namespace}"><soap:Body><d:CompleteResponse xmlns:d="urn:demo"><d:Result>accepted</d:Result></d:CompleteResponse></soap:Body></soap:Envelope>`);return;
  }
  if(req.url==='/soap-fault'){res.setHeader('Content-Type','text/xml');res.end('<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><s:Fault><faultcode>s:Server</faultcode><faultstring>Demo fault</faultstring></s:Fault></s:Body></s:Envelope>');return;}
  if(req.url==='/stats'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify({operations:operations.size}));return;}
  res.setHeader('Content-Type','application/json');res.end(JSON.stringify(operations.get(key)));
}).listen(Number(process.env.DEMO_HTTP_PORT||5091),'127.0.0.1',()=>console.log(`Demo REST, SOAP and SMS: http://127.0.0.1:${process.env.DEMO_HTTP_PORT||5091}`));

net.createServer(socket=>{
  socket.setEncoding('utf8');socket.write('220 localhost demo SMTP\r\n');let buffer='',data=false;
  socket.on('data',chunk=>{buffer+=chunk;while(buffer.includes('\r\n')){const index=buffer.indexOf('\r\n'),line=buffer.slice(0,index);buffer=buffer.slice(index+2);
    if(data){if(line==='.') {data=false;socket.write('250 Accepted locally\r\n');}continue;}
    if(/^EHLO|^HELO/.test(line))socket.write('250 localhost\r\n');
    else if(/^DATA/.test(line)){data=true;socket.write('354 End with dot\r\n');}
    else if(/^QUIT/.test(line)){socket.end('221 Bye\r\n');}
    else socket.write('250 OK\r\n');
  }});socket.on('error',()=>{});
}).listen(Number(process.env.DEMO_SMTP_PORT||2525),'127.0.0.1',()=>console.log(`Demo SMTP: 127.0.0.1:${process.env.DEMO_SMTP_PORT||2525}`));
