(() => {
  'use strict';
  const $ = (s, root = document) => root.querySelector(s);
  const $$ = (s, root = document) => Array.from(root.querySelectorAll(s));
  const selected = new Set();
  let selectionStore=null;
  if($('[data-ev-live-orders]')){
    const query=new URLSearchParams(location.search);['pagina','porPagina','orden'].forEach(k=>query.delete(k));query.sort();
    selectionStore='ev.selection:'+(document.body.dataset.evUser||'')+':'+location.pathname+'?'+query;
    try{const actorStore='ev.selection.active:'+(document.body.dataset.evUser||'');const prior=sessionStorage.getItem(actorStore);if(prior&&prior!==selectionStore)sessionStorage.removeItem(prior);sessionStorage.setItem(actorStore,selectionStore);const ids=JSON.parse(sessionStorage.getItem(selectionStore)||'[]');ids.filter(id=>/^\d+$/.test(id)).slice(0,1000).forEach(id=>selected.add(String(id)));}catch{}
  }
  const hiddenColumns = new Set();
  let loading = false, lastSuccess = Date.now(), activeMapFilter = 'Todos', map, markers = [], fleet = [];
  const token = () => $('input[name="__RequestVerificationToken"]')?.value || '';
  const uid = () => {if(crypto.randomUUID)return crypto.randomUUID();const bytes=crypto.getRandomValues(new Uint8Array(16));bytes[6]=(bytes[6]&15)|64;bytes[8]=(bytes[8]&63)|128;const hex=Array.from(bytes,b=>b.toString(16).padStart(2,'0')).join('');return hex.slice(0,8)+'-'+hex.slice(8,12)+'-'+hex.slice(12,16)+'-'+hex.slice(16,20)+'-'+hex.slice(20);};
  let fieldSerial=0;
  function associateLabels(root=document){
    for(const field of $$('.ev-field',root)){
      const label=$('label',field),controls=$$('input:not([type="hidden"]),select,textarea',field),control=controls.find(c=>!c.hasAttribute('aria-label'))||controls[0];if(!label||!control)continue;
      if(!control.id){let id;do{id='ev-field-'+(++fieldSerial);}while(document.getElementById(id));control.id=id;}
      label.htmlFor=control.id;if(!label.id)label.id=control.id+'-label';
      const help=$('small',field);if(help&&!help.id)help.id=control.id+'-help';
      for(const item of controls){if(!item.hasAttribute('aria-label'))item.setAttribute('aria-labelledby',label.id);if(help)item.setAttribute('aria-describedby',help.id);}
    }
  }
  const displayLabel = value => ({Devolucion:'Devolución',Reintento:'Reintento',EnRetorno:'En retorno',DevolucionAutorizada:'Devolución autorizada',ReintentoAutorizado:'Reintento autorizado',EnRuta:'En ruta',EnAtencion:'En atención',EnRevision:'En revisión',FinalizadaConDevoluciones:'Cerrada con devoluciones',Continuar:'Permitir continuar',PausarParada:'Pausar la parada afectada',PausarRuta:'Pausar la ruta afectada'}[value]??value??'—');
  window.Envirapido = {
    synced() { lastSuccess=Date.now();ticks(); },
    async api(path, body, method = 'POST') {
      const response = await fetch('/api/operaciones/' + path, { method, credentials: 'same-origin', headers: {'Content-Type':'application/json', 'X-CSRF-TOKEN':token(), 'RequestVerificationToken':token(), 'Accept':'application/json'}, body:body == null ? undefined : JSON.stringify(body), cache:'no-store' });
      let data; try { data = await response.json(); } catch { data = null; }
      if (!response.ok) {const error=new Error(data?.message || data?.error || data?.title || `No se pudo completar la operación (${response.status}).`);error.status=response.status;error.solicitudRechazada=data?.solicitudRechazada===true;throw error;}
      return data;
    }, uid, $, $$, associateLabels, displayLabel,
    error(message, root = $('#evMain')) {
      let box = $('[data-ev-error]',root); if (!box) { box = document.createElement('div'); box.className = 'ev-alert red'; box.dataset.evError = ''; box.setAttribute('role','alert'); root.prepend(box); } box.textContent = message; box.scrollIntoView({block:'nearest'});
    }
  };
  function toggleMenu(open) { document.body.classList.toggle('menu-open',open); $('[data-ev-menu]')?.setAttribute('aria-expanded',String(open)); const overlay=$('[data-ev-close-menu]'); if(overlay) overlay.hidden=!open; }
  $('[data-ev-menu]')?.addEventListener('click',()=>toggleMenu(!document.body.classList.contains('menu-open')));
  $('[data-ev-close-menu]')?.addEventListener('click',()=>toggleMenu(false));
  document.addEventListener('keydown',e=>{if(e.key==='Escape') toggleMenu(false);});
  window.addEventListener('resize',()=>{if(innerWidth>720)toggleMenu(false);});

  function syncSelection() {
    if(selectionStore)try{sessionStorage.setItem(selectionStore,JSON.stringify([...selected]));}catch{}
    $$('[data-ev-select-order]').forEach(c=>{c.checked=selected.has(c.value);c.closest('tr')?.classList.toggle('selected',c.checked);});
    const bar=$('[data-ev-selection]'); if(bar)bar.hidden=selected.size===0;
    const count=$('[data-ev-selection-count]'); if(count)count.textContent=String(selected.size);
    const all=$$('[data-ev-select-order]'), page=$('[data-ev-select-page]');
    if(page){page.checked=all.length>0&&all.every(c=>c.checked);page.indeterminate=all.some(c=>c.checked)&&!page.checked;}
    hiddenColumns.forEach(name=>$$(`[data-col="${name}"]`).forEach(c=>c.hidden=true));
  }
  function openAssign(ids,label) {
    const dialog=$('#evAssignDialog'); if(!dialog)return;
    dialog.dataset.orderIds=ids.join(',');dialog.dataset.bulkAssignment=String(ids.length>1); $('input[name="ordenId"]',dialog).value=ids[0]||'';
    $('[data-ev-assign-label]',dialog).textContent=label;
    const notice=$('[data-ev-bulk-notice]',dialog); if(notice)notice.hidden=ids.length<2;
    const errors=$('[data-ev-assign-errors]',dialog);if(errors)errors.textContent='';dialog.showModal();
  }
  document.addEventListener('click',e=>{
    const assign=e.target.closest('[data-ev-assign]');if(assign)openAssign([assign.dataset.evAssign],assign.dataset.orderLabel||'Pedido');
    const cancel=e.target.closest('[data-ev-cancel]');if(cancel){const dialog=$('#evCancelDialog'),form=$('form',dialog);form.reset();form.elements.ordenId.value=cancel.dataset.evCancel;form.elements.operacionId.value=uid();$('[data-ev-cancel-error]',dialog).textContent='';try{const pending=JSON.parse(sessionStorage.getItem(`ev.cancel:${document.body.dataset.evUser}:${cancel.dataset.evCancel}`)||'null');if(pending){form.elements.operacionId.value=pending.operacionId;form.elements.motivo.value=pending.motivo;}}catch{}dialog.showModal();}
    if(e.target.closest('[data-ev-close-dialog]'))e.target.closest('dialog')?.close();
    if(e.target.closest('[data-ev-bulk-assign]'))openAssign([...selected],`${selected.size} pedidos seleccionados`);
    if(e.target.closest('[data-ev-deselect]')){selected.clear();syncSelection();}
    if(e.target.closest('[data-ev-export-selection]')){const u=new URL('/Operaciones/Exportar',location.origin);u.searchParams.set('ids',[...selected].join(','));location.href=u;}
    if(e.target.closest('[data-ev-labels]')){const u=new URL('/Operaciones/Etiquetas',location.origin);u.searchParams.set('ids',[...selected].join(','));window.open(u,'_blank','noopener');}
    if(e.target.closest('[data-ev-print]'))window.print();
    const mode=e.target.closest('[data-queue-mode]');if(mode){$$('[data-queue-mode]').forEach(b=>b.classList.toggle('active',b===mode));$$('[data-queue-auto]').forEach(c=>c.hidden=mode.dataset.queueMode==='manual'?c.dataset.queueAuto==='true':c.dataset.queueAuto==='false');}
    const filter=e.target.closest('[data-map-filter]');if(filter){activeMapFilter=filter.dataset.mapFilter;$$('[data-map-filter]').forEach(b=>b.classList.toggle('active',b===filter));renderFleet();}
  });
  document.addEventListener('change',e=>{
    if(e.target.matches('[data-ev-select-order]')){e.target.checked?selected.add(e.target.value):selected.delete(e.target.value);syncSelection();}
    if(e.target.matches('[data-ev-select-page]')){$$('[data-ev-select-order]').forEach(c=>e.target.checked?selected.add(c.value):selected.delete(c.value));syncSelection();}
    if(e.target.matches('[data-ev-column]')){const col=e.target.dataset.evColumn;e.target.checked?hiddenColumns.delete(col):hiddenColumns.add(col);$$(`[data-col="${col}"]`).forEach(c=>c.hidden=!e.target.checked);}
    if(e.target.matches('[data-ev-page-size]'))location.href=e.target.value;
  });
  $('[data-ev-assign-form]')?.addEventListener('submit',async e=>{
    const dialog=e.target.closest('dialog'),ids=(dialog.dataset.orderIds||'').split(',').filter(Boolean);if(dialog.dataset.bulkAssignment!=='true')return;
    e.preventDefault();const button=$('button[type="submit"]',e.target);button.disabled=true;const failures=[],pending=[];
    for(const id of ids){const body=new FormData(e.target);body.set('ordenId',id);try{const r=await fetch(e.target.action,{method:'POST',body,headers:{Accept:'application/json'},credentials:'same-origin'});const result=await r.json();if(!r.ok||!result.ok)throw new Error(result.error||'No se pudo asignar.');selected.delete(id);}catch(err){pending.push(id);failures.push(`#${id}: ${err.message}`);}}
    dialog.dataset.orderIds=pending.join(',');$('input[name="ordenId"]',dialog).value=pending[0]||'';
    button.disabled=false;if(failures.length){$('[data-ev-assign-errors]',dialog).textContent=failures.join(' · ');$('[data-ev-assign-label]',dialog).textContent=`${pending.length} pedidos pendientes de asignar`;syncSelection();}else location.reload();
  });
  $('[data-ev-cancel-form]')?.addEventListener('submit',async e=>{e.preventDefault();const form=e.target,id=form.elements.ordenId.value,body={motivo:form.elements.motivo.value,operacionId:form.elements.operacionId.value},key=`ev.cancel:${document.body.dataset.evUser}:${id}`,button=$('button[type="submit"]',form),error=$('[data-ev-cancel-error]',form);button.disabled=true;try{const prior=JSON.parse(sessionStorage.getItem(key)||'null');if(prior&&JSON.stringify(prior)!==JSON.stringify(body))throw new Error('Reintenta el intento pendiente con sus mismos datos antes de cambiar el motivo.');sessionStorage.setItem(key,JSON.stringify(body));await window.Envirapido.api(`ordenes/${id}/cancelar`,body);sessionStorage.removeItem(key);location.reload();}catch(err){if(err.solicitudRechazada||[400,401,403,404,422].includes(err.status))sessionStorage.removeItem(key);error.className='ev-alert red';error.textContent=err.message;button.disabled=false;}});

  function updateSummary(holder){let stats;try{stats=JSON.parse($('[data-ev-live-summary]',holder)?.textContent||'null');}catch{}if(!stats)return;for(const node of $$('[data-ev-value]')){const key=node.dataset.evValue,v=stats[key];node.textContent=key==='puntualidad'?(v==null?'—':new Intl.NumberFormat('es-DO',{maximumFractionDigits:1}).format(v)+'%'):new Intl.NumberFormat('es-DO').format(v??0);}const count=$('[data-ev-queue-count]');if(count)count.textContent=String(stats.porAsignar);const queue=$('.ev-queue-list');if(!queue)return;const mode=$('[data-queue-mode].active')?.dataset.queueMode||'auto';queue.replaceChildren();for(const p of stats.cola||[]){const row=document.createElement('article');row.className='ev-queue-item';row.dataset.queueAuto=String(p.auto);row.hidden=mode==='manual'?p.auto:!p.auto;row.innerHTML='<svg class="ev-icon"><use href="#ev-box"></use></svg><div class="ev-queue-info"><strong></strong><p></p><small></small></div><div class="ev-queue-right"><span class="ev-badge amber">Por asignar</span><button type="button" class="ev-btn primary small" data-ev-assign>Asignar</button></div>';$('strong',row).textContent='EV-'+p.codigo;$('p',row).textContent=p.comercio+' → '+p.destinatario;const note=$('small',row);note.append(document.createTextNode(p.auto?'Ofertada a repartidores · ':'Asignación manual · '));const time=document.createElement('time');time.dateTime=p.creadoUtc;time.dataset.evWait='';note.append(time);const button=$('button',row);button.dataset.evAssign=p.id;button.dataset.orderLabel='EV-'+p.codigo;queue.append(row);}if(!stats.cola?.length){const empty=document.createElement('div');empty.className='ev-empty';empty.textContent='Sin pedidos por asignar.';queue.append(empty);}}

  function ticks(){
    $$('[data-ev-wait]').forEach(t=>{const n=Math.max(0,Math.floor((Date.now()-new Date(t.dateTime).getTime())/60000));t.textContent=n<60?`${n} min`:`${Math.floor(n/60)} h ${n%60} min`;});
    $$('[data-ev-freshness]').forEach(t=>{const at=new Date(t.closest('[data-updated]')?.dataset.updated||lastSuccess).getTime();const n=Math.max(0,Math.floor((Date.now()-at)/1000));t.textContent=n<3?'Actualizado ahora':`Actualizado hace ${n} s`;});
    const badge=$('[data-ev-sync]');if(badge){const stale=Date.now()-lastSuccess>30000;badge.classList.toggle('stale',stale);$('span',badge).textContent=stale?'Sin actualizar':'Sincronizado';}
  }
  async function refresh(){
    if(loading||document.hidden||$('dialog[open]')||document.activeElement?.matches('input,textarea,select')||$('details[open]'))return;
    const holder=$('[data-ev-live-orders]');if(!holder)return;loading=true;
    const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),12000);
    try{const query=new URLSearchParams(location.search);query.set('seccion',holder.dataset.section);const r=await fetch('/Operaciones/Fragmento?'+query,{cache:'no-store',credentials:'same-origin',headers:{'X-Requested-With':'XMLHttpRequest'},signal:controller.signal});if(!r.ok||r.redirected)throw new Error('La sesión o la consulta requiere actualizarse.');const html=await r.text();if(!html.includes('data-ev-orders'))throw new Error('Respuesta de actualización inválida.');holder.innerHTML=html;syncSelection();updateSummary(holder);lastSuccess=Date.now();}catch{}finally{clearTimeout(timeout);loading=false;ticks();}
  }
  setInterval(ticks,1000);setInterval(refresh,5000);document.addEventListener('visibilitychange',()=>{if(!document.hidden){refresh();refreshFleet();}});ticks();syncSelection();

  function readJson(id,fallback){try{return JSON.parse($('#'+id)?.textContent||'null')||fallback;}catch{return fallback;}}
  fleet=readJson('evMapData',[]);
  let fleetLoading=false,routeLines=[],routeMarkers=[],selectedOrder=null,mapRoutes=readJson('evMapRoutes',[]);
  function drawRoutes(orderId=null){
    if(!map)return;routeLines.forEach(line=>line.setMap(null));routeMarkers.forEach(marker=>marker.setMap(null));routeLines=[];routeMarkers=[];
    const shown=orderId?mapRoutes.filter(route=>route.ordenIds.includes(Number(orderId))):mapRoutes.slice(0,1),encoded=[];
    for(const route of shown){
      if(!$('#evOrderMap'))for(const point of route.puntos){
        if(!point.puedeVerDestino||point.latitud==null||point.longitud==null||point.excluidoPorRetorno||point.excluidoPorCancelacion)continue;
        const marker=new google.maps.Marker({map,position:{lat:point.latitud,lng:point.longitud},title:route.codigo+' · '+displayLabel(point.tipo)+' · '+point.nombre,label:{text:point.tipo==='Recogida'?'R':point.tipo==='Devolucion'?'↶':String(point.posicion),color:'#fff',fontSize:'11px'},icon:{path:google.maps.SymbolPath.CIRCLE,scale:10,fillColor:point.tipo==='Devolucion'?'#2569a9':point.completado?'#18a15a':point.tipo==='Recogida'?'#1f9b4f':'#db4a4c',fillOpacity:1,strokeColor:'#fff',strokeWeight:2}});routeMarkers.push(marker);
      }
      if(route.geometria)try{encoded.push(...JSON.parse(route.geometria));}catch{}
    }
    const tracking=readJson('evTrackingGeometry',null);if(tracking)try{encoded.splice(0,encoded.length,...JSON.parse(tracking));}catch{}
    if(google.maps.geometry?.encoding)for(const segment of encoded)routeLines.push(new google.maps.Polyline({map,path:google.maps.geometry.encoding.decodePath(segment),strokeColor:'#008589',strokeOpacity:.9,strokeWeight:4}));
  }
  document.addEventListener('click',event=>{const row=event.target.closest('tr[data-order-id]');if(row&&!event.target.closest('a,button,input,details')){selectedOrder=row.dataset.orderId;drawRoutes(selectedOrder);}});
  async function refreshFleet(){if(fleetLoading||document.hidden||!$('#evMapData'))return;fleetLoading=true;const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),12000);try{const r=await fetch('/Operaciones/MapaDatos',{credentials:'same-origin',cache:'no-store',signal:controller.signal});if(r.ok&&!r.redirected){const data=await r.json();fleet=data.pilotos||[];if($('#evOrderMap')){const allowed=readJson('evMapData',[]).map(p=>p.id);fleet=fleet.filter(p=>allowed.includes(p.id));}if(!$('#evOrderMap')&&Array.isArray(data.rutas)){mapRoutes=data.rutas;drawRoutes(selectedOrder);}renderFleet();}}catch{}finally{clearTimeout(timeout);fleetLoading=false;updateGpsBadge();}}
  function renderFleet(){
    if(!map||!window.google)return;markers.forEach(marker=>marker.setMap(null));markers=[];
    const shown=fleet.filter(p=>p.latitud!=null&&p.longitud!=null&&(activeMapFilter==='Todos'||(activeMapFilter==='Incidencia'?p.conIncidencia:p.estado===activeMapFilter)));
    const zoom=map.getZoom()??11,cell=zoom<14?0.008*Math.pow(2,14-zoom):0,groups=new Map();
    for(const pilot of shown){const key=cell?Math.floor(pilot.latitud/cell)+':'+Math.floor(pilot.longitud/cell):'pilot:'+pilot.id;if(!groups.has(key))groups.set(key,[]);groups.get(key).push(pilot);}
    for(const group of groups.values()){
      const p=group[0],count=group.length,position={lat:group.reduce((sum,x)=>sum+x.latitud,0)/count,lng:group.reduce((sum,x)=>sum+x.longitud,0)/count},fresh=group.filter(x=>x.activo&&x.gpsVigente).length;
      const marker=new google.maps.Marker({map,position,title:count>1?count+' posiciones de flota · '+fresh+' GPS vigentes':p.nombre+' · '+p.estado+' · '+(p.gpsVigente?'GPS vigente':'GPS sin actualizar'),label:{text:count>1?String(count):'↗',color:'#fff',fontSize:count>1?'12px':'15px'},icon:{path:google.maps.SymbolPath.CIRCLE,fillColor:fresh?'#008589':'#87949c',fillOpacity:1,strokeWeight:3,strokeColor:'#fff',scale:count>1?17:13}});
      if(count>1)marker.addListener('click',()=>{map.setCenter(position);map.setZoom(Math.min(18,zoom+2));});
      else{const info=document.createElement('div'),title=document.createElement('strong');title.textContent=p.nombre;info.append(title,document.createElement('br'),document.createTextNode(p.estado+' · '+p.carga+' órdenes · '+(p.gpsVigente?'GPS vigente':'Última posición sin actualizar')));const popup=new google.maps.InfoWindow({content:info});marker.addListener('click',()=>popup.open({map,anchor:marker}));}
      markers.push(marker);
    }
    const fresh=$('[data-ev-map-fresh]');if(fresh)fresh.textContent=fleet.filter(p=>p.gpsVigente).length+' GPS vigentes';
  }
  async function initMap(){const box=$('[data-ev-map]');if(!box)return;const empty=$('.ev-map-empty',box), key=box.dataset.mapApiKey;
    if(!key||key.toUpperCase().includes('REEMPLAZAR')){if(empty){$('strong',empty).textContent='Mapa pendiente de configuración';$('span',empty).textContent='Configura la clave de mapas para mostrar las ubicaciones reales.';}return;}
    window.evGoogleReady=()=>{const target=$('.ev-map-content',box);map=new google.maps.Map(target,{center:{lat:18.4861,lng:-69.9312},zoom:11,mapTypeControl:true,streetViewControl:false,fullscreenControl:true,gestureHandling:'cooperative'});if(empty)empty.hidden=true;renderFleet();const points=readJson('evOrderMap',null),bounds=new google.maps.LatLngBounds();let count=0;
      if(points){for(const [type,p]of Object.entries(points)){if(p&&p.lat!=null&&p.lng!=null){new google.maps.Marker({map,position:{lat:p.lat,lng:p.lng},title:p.nombre||type,label:type==='pickup'?'R':'E'});bounds.extend({lat:p.lat,lng:p.lng});count++;}}}
      markers.forEach(m=>{bounds.extend(m.getPosition());count++;});if(count>1)map.fitBounds(bounds,40);else if(count===1){map.setCenter(bounds.getCenter());map.setZoom(14);}lastSuccess=Date.now();drawRoutes();map.addListener('zoom_changed',()=>renderFleet());
    };
    if(window.google?.maps){window.evGoogleReady();return;}const script=document.createElement('script');script.src='https://maps.googleapis.com/maps/api/js?key='+encodeURIComponent(key)+'&callback=evGoogleReady&loading=async&libraries=geometry';script.async=true;script.onerror=()=>{if(empty){empty.hidden=false;$('strong',empty).textContent='No se pudo cargar el mapa';$('span',empty).textContent='La tabla y los datos de la operación siguen disponibles.';}};document.head.append(script);
  }
  function updateGpsBadge(){
    let changed=false;for(const pilot of fleet){const at=pilot.gpsUtc?new Date(pilot.gpsUtc).getTime():0,fresh=!!at&&Date.now()-at<=(pilot.gpsFrescuraSegundos||120)*1000&&pilot.latitud!=null&&pilot.longitud!=null;if(fresh!==pilot.gpsVigente){pilot.gpsVigente=fresh;changed=true;}}
    const badge=$('[data-ev-gps-badge]');if(badge){const fresh=fleet[0]?.gpsVigente===true;badge.classList.toggle('green',fresh);badge.classList.toggle('amber',!fresh);badge.textContent=fresh?'GPS actualizado':'GPS sin actualizar';}if(changed)renderFleet();
  }
  let trackingBusy=false;
  const initialTrackingState=JSON.stringify(readJson('evTrackingState',null));
  async function refreshTracking(){
    const target=$('[data-ev-order-tracking]');if(!target||trackingBusy||document.hidden||$('dialog[open]')||document.activeElement?.matches('input,textarea,select'))return;
    trackingBusy=true;const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),12000);
    try{const url='/Operaciones/Seguimiento/'+target.dataset.evOrderTracking+(target.dataset.paradaId?'?paradaId='+encodeURIComponent(target.dataset.paradaId):'');const response=await fetch(url,{credentials:'same-origin',cache:'no-store',signal:controller.signal});if(!response.ok||response.redirected)throw new Error('Seguimiento no disponible.');const result=await response.json(),nextState=JSON.stringify({estado:result.estado,pilotoId:result.pilotoId,paradas:result.paradas});if(initialTrackingState!=='null'&&initialTrackingState!==nextState){location.reload();return;}
      const terminal=['Entregada','Devuelta','FinalizadaConDevoluciones','Cancelada'].includes(result.estado),estimate=terminal?null:result.seguimiento,eta=$('[data-ev-tracking-eta]'),minutes=$('[data-ev-tracking-minutes]'),km=$('[data-ev-tracking-km]');if(eta)eta.textContent=terminal?'Finalizado':estimate?new Intl.DateTimeFormat('es-DO',{hour:'2-digit',minute:'2-digit',timeZone:'America/Santo_Domingo'}).format(new Date(new Date(estimate.calculadoAt).getTime()+estimate.minutos*60000)):'Sin estimación';if(minutes)minutes.textContent=estimate?Math.ceil(estimate.minutos)+' min':'—';if(km)km.textContent=estimate?new Intl.NumberFormat('es-DO',{maximumFractionDigits:1}).format(estimate.distanciaKm)+' km':'—';const geometry=$('#evTrackingGeometry');if(geometry){geometry.textContent=JSON.stringify(estimate?.geometria||null);drawRoutes(selectedOrder);}lastSuccess=Date.now();
    }catch{}finally{clearTimeout(timeout);trackingBusy=false;}
  }
  setInterval(refreshFleet,5000);setInterval(updateGpsBadge,1000);setInterval(refreshTracking,5000);document.addEventListener('visibilitychange',()=>{if(!document.hidden)refreshTracking();});
  let etaBusy=false;
  async function refreshEtas(){
    if(etaBusy||document.hidden||$('dialog[open]')||$('details[open]'))return;
    const rows=$$('tr[data-order-id]').filter(row=>$('[data-ev-eta]',row));if(!rows.length)return;etaBusy=true;
    const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),20000);
    try{
      const ids=rows.slice(0,50).map(row=>row.dataset.orderId);const response=await fetch('/Operaciones/Seguimientos?ids='+encodeURIComponent(ids.join(',')),{credentials:'same-origin',cache:'no-store',signal:controller.signal});
      if(!response.ok||response.redirected){let error;try{error=await response.json();}catch{}throw new Error(error?.error||'No se pudo actualizar ETA.');}
      const result=await response.json();
      for(const item of result.pedidos){const row=$$('tr[data-order-id]').find(r=>r.dataset.orderId===String(item.ordenId)),cell=row&&$('[data-ev-eta]',row);if(!cell)continue;const estimate=item.seguimiento;cell.textContent=item.estado==='Entregada'?'Completado':item.esTerminal?'Finalizado':estimate?Math.ceil(estimate.minutos)+' min':'Sin estimación';cell.title=item.esTerminal?'Operación finalizada':estimate?'Llegada estimada '+new Intl.DateTimeFormat('es-DO',{hour:'2-digit',minute:'2-digit',timeZone:'America/Santo_Domingo'}).format(new Date(estimate.llegadaUtc))+' · cálculo '+new Intl.DateTimeFormat('es-DO',{hour:'2-digit',minute:'2-digit',timeZone:'America/Santo_Domingo'}).format(new Date(estimate.calculadoAt)):'Sin datos suficientes de proveedor o GPS';cell.classList.remove('ev-text-red');}
    }catch(error){for(const row of rows){const cell=$('[data-ev-eta]',row);if(cell){cell.textContent='Sin actualizar';cell.title=error.name==='AbortError'?'La estimación tardó demasiado; se reintentará.':error.message;cell.classList.add('ev-text-red');}}}
    finally{clearTimeout(timeout);etaBusy=false;}
  }
  setInterval(refreshEtas,5000);document.addEventListener('visibilitychange',()=>{if(!document.hidden)refreshEtas();});
  associateLabels();initMap();refreshEtas();
})();
