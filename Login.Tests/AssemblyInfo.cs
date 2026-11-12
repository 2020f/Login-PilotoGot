// LocalDB es compartido por el host QA y cada caso crea su propia base.
// Serializar las clases evita agotar sus conexiones/DDL en máquinas de QA;
// las carreras reales dentro de cada caso siguen ejecutándose con Task.WhenAll.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
