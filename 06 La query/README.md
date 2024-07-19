Una query para sacar un estado de la Base de Datos.


```mysql
SELECT query.*,
       CASE
           WHEN lower(query.description) = 'rejectedbysdi' OR
                lower(query.description) = 'rejectedcustomer' THEN
               CASE
                   WHEN (SELECT Count(*)
                         FROM it_tbuffer_hrefs
                         WHERE intreferenced_ut_type = 4001
                           AND intacceptance_level = 90
                           AND logfirm = '1'
                           AND intrejection_type = 220
                           AND intfirm_acceptance_level = 90) > 0 THEN
                       'Rechazada'
                   ELSE
                       CASE
                           WHEN (query.intcfue_state = 8
                               AND query.intreplacement_state = 1
                               AND (SELECT Count(*)
                                    FROM it_tbuffer_hrefs
                                    WHERE intreferenced_ut_type = 4001
                                      AND intacceptance_level = 90
                                      AND logfirm = '1'
                                      AND intrejection_type = 220
                                      AND intfirm_acceptance_level = 90) <= 0)
                               THEN
                               'No enviada'
                           WHEN (query.intcfue_state = 8
                               AND query.intreplacement_state = 1
                               AND (SELECT Count(*)
                                    FROM it_tbuffer_hrefs
                                    WHERE intreferenced_ut_type = 4001
                                      AND intacceptance_level = 90
                                      AND logfirm = '1'
                                      AND intrejection_type = 220
                                      AND intfirm_acceptance_level = 90) >= 1)
                               THEN
                               'Rechazada'
                           ELSE ''
                           END
                   END
           WHEN lower(query.description) = 'senttosdi' OR
                lower(query.description) = 'tobesent' THEN
               'Pendiente de firma, sin entregar al SDI'
           WHEN lower(query.description) = 'delivered' THEN
               'Ha sido entregado al SDI, esperando respuesta'
           WHEN lower(query.description) = 'notpossibletodeliver' THEN
               'No se puede entregar, con acuse de no se pudo entregar'
           WHEN (query.description IS NULL OR query.description = '') AND
                query.LOGONLY_ARCHIVED = 1 THEN
               'Archivado'
           ELSE
               'No se ha podido determinar el estado'
           END AS ESTADO
FROM (SELECT Itbs.inttid,
             Itbs.strref,
             Itbs.tstime_creation,
             Itbs.logonly_archived,
             Itbs.intcfue_state,
             Itbs.intreplacement_state,
             (SELECT description
              FROM (SELECT Iges.description
                    FROM it_gateways_external_states Iges
                    WHERE Iges.inttid = Itbs.inttid
                    ORDER BY Iges.tstime_creation DESC)
              WHERE rownum <= 1) AS Description
      FROM it_tbuffer_search Itbs
      WHERE Itbs.strref IN (
          '1234'
          )
        AND Itbs.intsender_root = 2471805
        AND Itbs.inttype = 4001
        AND Itbs.intcfue_state != 10
        AND Itbs.intreplacement_state != 4) query; 
```

